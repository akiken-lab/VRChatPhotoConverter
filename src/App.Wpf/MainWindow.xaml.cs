using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Core;
using Core.Abstractions;
using Core.Models;
using Infrastructure.Config;
using Infrastructure.Conversion;
using Infrastructure.History;
using Infrastructure.Logging;
using Infrastructure.Monitoring;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace PhotoConverterApp;

public partial class MainWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly IProcessingHistoryStore _historyStore;
    private readonly IGameExitMonitor _monitor = new GameExitMonitor();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly bool _startMinimized;

    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _trayMonitorItem;

    private ILogService _log;
    private IPhotoConversionService _converter;
    private AppConfig _config = new();
    private AppLogLevel _currentLevel = AppLogLevel.Information;

    private bool _allowExit;
    private bool _isMinimizingToTray;
    private bool _hasShownMinimizeAnnouncement;
    private bool _hasShownCloseAnnouncement;
    private bool _suppressToggleHandler;
    private int _lastScanned;
    private int _lastConverted;
    private int _lastErrors;
    private DateTimeOffset? _lastRunAt;
    private string? _lastError;

    public MainWindow()
    {
        _startMinimized = Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));

        _historyStore = new SqliteProcessingHistoryStore(_configStore.GetDataDirectory());
        _log = new LogService(_configStore.GetLogDirectory(), AppLogLevel.Information);
        _converter = new PhotoConversionService(_log, _historyStore);
        _monitor.GameExited += MonitorOnGameExited;

        _trayMonitorItem = new Forms.ToolStripMenuItem("監視を開始", null, (_, _) => Dispatcher.InvokeAsync(ToggleMonitoringFromTray));
        _trayIcon = BuildTrayIcon();
        _trayIcon.DoubleClick += (_, _) => Dispatcher.InvokeAsync(RestoreFromTray);

        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _config = Normalize(await _configStore.LoadAsync());

        if (_config.Profiles.Count == 0)
        {
            var wizard = new FirstRunWizardWindow { Owner = this };
            var wizardResult = wizard.ShowDialog();
            if (wizardResult == true && wizard.CreatedConfig is not null)
            {
                _config = Normalize(wizard.CreatedConfig);
                _config.MonitorEnabledOnStartup = wizard.StartMonitoringAfterFinish;
                if (!wizard.ConvertExistingFilesOnFirstSetup)
                {
                    await MarkExistingFilesAsProcessedAsync(_config);
                }
                await _configStore.SaveAsync(_config);
            }
        }

        ApplyRuntime(_config.LogLevel);
        ApplyStartup(_config.LaunchOnWindowsStartup);
        if (_config.MonitorEnabledOnStartup)
        {
            StartMonitor();
        }

        UpdateStatusUi();
        RefreshLogTail();

        if (_startMinimized)
        {
            MinimizeToTray("startup");
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            MinimizeToTray("close");
            return;
        }

        _monitor.Stop();
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _runLock.Dispose();

        _config.MonitorEnabledOnStartup = _monitor.IsRunning;
        await _configStore.SaveAsync(_config);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_isMinimizingToTray)
        {
            return;
        }

        if (WindowState == WindowState.Minimized && IsVisible)
        {
            MinimizeToTray("minimize");
        }
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Profiles ??= new();
        config.WatchTargets ??= new();

        if (config.Profiles.Count == 0 && !string.IsNullOrWhiteSpace(config.SourceDir))
        {
            var migratedId = Guid.NewGuid().ToString("N");
            config.Profiles.Add(new ConversionProfile
            {
                Id = migratedId,
                Name = "VRChat_Default",
                SourceDir = config.SourceDir,
                JpegOutputDir = config.JpegOutputDir,
                PngArchiveDir = config.PngArchiveDir,
                PngHandlingMode = config.PngHandlingMode == PngHandlingMode.Delete ? PngHandlingMode.Delete : PngHandlingMode.Keep,
                JpegQuality = config.JpegQuality,
                IncludeSubdirectories = config.IncludeSubdirectories,
                DuplicatePolicy = DuplicatePolicy.Overwrite,
                DryRun = config.DryRun,
                RecentFileGuardSeconds = config.RecentFileGuardSeconds
            });
            config.DefaultProfileId = migratedId;
        }

        if (config.Profiles.Count > 0)
        {
            foreach (var profile in config.Profiles)
            {
                profile.PngHandlingMode = profile.PngHandlingMode == PngHandlingMode.Delete ? PngHandlingMode.Delete : PngHandlingMode.Keep;
                profile.PngArchiveDir = string.Empty;
                profile.DuplicatePolicy = DuplicatePolicy.Overwrite;
            }
            config.DefaultProfileId ??= config.Profiles[0].Id;
            foreach (var target in config.WatchTargets)
            {
                target.Mode = WatchTargetMode.ExeOnly;
                if (string.IsNullOrWhiteSpace(target.ProfileId))
                {
                    target.ProfileId = config.DefaultProfileId;
                }
                target.ProfileName = config.Profiles.FirstOrDefault(p => p.Id == target.ProfileId)?.Name ?? "VRChat_Default";
            }
        }

        return config;
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_trayMonitorItem);
        menu.Items.Add("今すぐ変換", null, (_, _) => Dispatcher.InvokeAsync(() => RunNowInternalAsync("tray_manual")));
        menu.Items.Add("JPEG出力先", null, (_, _) => Dispatcher.InvokeAsync(OpenOutputFolderInternal));
        menu.Items.Add("ログ", null, (_, _) => Dispatcher.InvokeAsync(OpenLogsInternal));
        menu.Items.Add("設定...", null, (_, _) => Dispatcher.InvokeAsync(OpenSettingsInternal));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Dispatcher.InvokeAsync(ExitFromTray));

        return new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "監視停止",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void ToggleMonitoringFromTray()
    {
        if (_monitor.IsRunning)
        {
            StopMonitor();
        }
        else
        {
            StartMonitor();
        }

        _ = SaveMonitorStateAsync();
    }

    private async Task MarkExistingFilesAsProcessedAsync(AppConfig config)
    {
        var total = CountExistingPngFiles(config);
        var markedCount = 0;
        if (total > 0)
        {
            ProgressTextBlock.Text = $"初回準備中: 既存PNGを処理済み登録しています (0/{total})";
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.SourceDir) || !Directory.Exists(profile.SourceDir))
            {
                continue;
            }

            var option = profile.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(profile.SourceDir, "*.png", option);
            }
            catch (Exception ex)
            {
                UiDiagnostics.LogException("SeedHistory_Enumerate", ex);
                continue;
            }

            foreach (var sourcePath in files)
            {
                try
                {
                    var lastWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
                    var size = new FileInfo(sourcePath).Length;
                    await _historyStore.MarkProcessedAsync(sourcePath, lastWriteUtc, size);
                    markedCount++;
                    if (total > 0 && (markedCount == total || markedCount % 25 == 0))
                    {
                        ProgressTextBlock.Text = $"初回準備中: 既存PNGを処理済み登録しています ({markedCount}/{total})";
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                catch (Exception ex)
                {
                    UiDiagnostics.LogException("SeedHistory_MarkProcessed", ex);
                }
            }
        }

        UiDiagnostics.LogMessage("SeedHistory", $"marked_existing_png={markedCount}");
        if (total > 0)
        {
            ProgressTextBlock.Text = $"初回準備完了: 既存PNG {markedCount}/{total} 枚を処理済み登録しました";
        }
    }

    private static int CountExistingPngFiles(AppConfig config)
    {
        var total = 0;
        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.SourceDir) || !Directory.Exists(profile.SourceDir))
            {
                continue;
            }

            try
            {
                var option = profile.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                total += Directory.EnumerateFiles(profile.SourceDir, "*.png", option).Count();
            }
            catch
            {
                // Ignore unreadable paths and fall back to the count gathered so far.
            }
        }

        return total;
    }

    private void MonitorToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleHandler)
        {
            return;
        }

        StartMonitor();
        _ = SaveMonitorStateAsync();
    }

    private void MonitorToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleHandler)
        {
            return;
        }

        StopMonitor();
        _ = SaveMonitorStateAsync();
    }

    private async Task SaveMonitorStateAsync()
    {
        _config.MonitorEnabledOnStartup = _monitor.IsRunning;
        await _configStore.SaveAsync(_config);
    }

    private void StartMonitor()
    {
        _monitor.Start(_config.WatchTargets);
        UpdateStatusUi();
    }

    private void StopMonitor()
    {
        _monitor.Stop();
        UpdateStatusUi();
    }

    private ConversionProfile? GetDefaultRule() => _config.Profiles.FirstOrDefault(p => p.Id == _config.DefaultProfileId) ?? _config.Profiles.FirstOrDefault();

    private async Task RunNowInternalAsync(string trigger)
    {
        var rule = GetDefaultRule();
        if (rule is null)
        {
            System.Windows.MessageBox.Show("ルールが未作成です。設定から作成してください。", "ルール未設定", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        await RunRulesAsync(trigger, new[] { rule });
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        await RunNowInternalAsync("manual");
    }

    private async Task RunRulesAsync(string trigger, IReadOnlyList<ConversionProfile> rules)
    {
        if (!await _runLock.WaitAsync(0))
        {
            return;
        }

        RunNowButton.IsEnabled = false;
        ProgressTextBlock.Text = "実行中...";

        try
        {
            var totalScanned = 0;
            var totalConverted = 0;
            var totalErrors = 0;
            string? firstError = null;

            foreach (var rule in rules)
            {
                var runtimeConfig = BuildRuntimeConfig(rule);
                var progress = new Progress<ConversionProgress>(p =>
                {
                    ProgressTextBlock.Text = $"実行中: {Path.GetFileName(p.CurrentFile)} ({p.ProcessedFiles}/{Math.Max(p.TotalFiles, 1)})";
                });

                var result = await _converter.RunAsync(runtimeConfig, trigger, progress);
                totalScanned += result.ScannedCount;
                totalConverted += result.ConvertedCount;
                totalErrors += result.ErrorCount;

                if (firstError is null && result.Errors.Count > 0)
                {
                    firstError = result.Errors[0];
                }
            }

            _lastScanned = totalScanned;
            _lastConverted = totalConverted;
            _lastErrors = totalErrors;
            _lastError = firstError;
            _lastRunAt = DateTimeOffset.Now;

            RefreshLogTail();
            UpdateStatusUi();
            ProgressTextBlock.Text = "処理完了";
        }
        catch (Exception ex)
        {
            _lastErrors++;
            _lastError = ex.Message;
            _lastRunAt = DateTimeOffset.Now;
            UpdateStatusUi();
            ProgressTextBlock.Text = "処理エラー";
        }
        finally
        {
            RunNowButton.IsEnabled = true;
            _runLock.Release();
        }
    }

    private AppConfig BuildRuntimeConfig(ConversionProfile rule)
    {
        return new AppConfig
        {
            SourceDir = rule.SourceDir,
            JpegOutputDir = rule.JpegOutputDir,
            PngArchiveDir = string.Empty,
            PngHandlingMode = rule.PngHandlingMode,
            JpegQuality = rule.JpegQuality,
            IncludeSubdirectories = rule.IncludeSubdirectories,
            DuplicatePolicy = DuplicatePolicy.Overwrite,
            DryRun = rule.DryRun,
            RecentFileGuardSeconds = rule.RecentFileGuardSeconds,
            LogLevel = _config.LogLevel
        };
    }

    private void MonitorOnGameExited(object? sender, string processName)
    {
        _ = Dispatcher.InvokeAsync(async () => await HandleGameExitedAsync(processName));
    }

    private async Task HandleGameExitedAsync(string processName)
    {
        var matchingRules = _config.WatchTargets
            .Where(t => string.Equals(Path.GetFileName(t.ExeName), Path.GetFileName(processName), StringComparison.OrdinalIgnoreCase))
            .Select(t => _config.Profiles.FirstOrDefault(p => p.Id == (t.ProfileId ?? _config.DefaultProfileId)))
            .Where(p => p is not null)
            .Cast<ConversionProfile>()
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .ToList();

        if (matchingRules.Count == 0)
        {
            return;
        }

        var delay = matchingRules.Max(p => p.RecentFileGuardSeconds) + 1;
        if (delay > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }

        await RunRulesAsync($"game_exit:{processName}", matchingRules);
    }

    private void UpdateStatusUi()
    {
        var isRunning = _monitor.IsRunning;

        _suppressToggleHandler = true;
        MonitorToggleButton.IsChecked = isRunning;
        MonitorToggleButton.Content = isRunning ? "監視中" : "停止中";
        _suppressToggleHandler = false;

        StatusBadgeTextBlock.Text = isRunning ? "監視中" : "停止中";
        StatusBadge.Background = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(isRunning ? "#D1FADF" : "#FEE2E2"));
        StatusBadgeTextBlock.Foreground = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(isRunning ? "#0E7A43" : "#B42318"));
        StatusSummaryTextBlock.Text = isRunning ? "ゲーム終了を監視しています。" : "監視は停止しています。";

        _trayMonitorItem.Checked = isRunning;
        _trayMonitorItem.Text = isRunning ? "監視を停止" : "監視を開始";

        var rule = GetDefaultRule();
        ActiveRuleTextBlock.Text = rule?.Name ?? "未設定";

        var target = _config.WatchTargets.FirstOrDefault(t => string.Equals(t.ProfileId, rule?.Id, StringComparison.OrdinalIgnoreCase))
                     ?? _config.WatchTargets.FirstOrDefault();
        var exePath = string.IsNullOrWhiteSpace(target?.ExeName) ? "未設定" : target.ExeName;
        TargetExeTextBlock.Text = exePath;
        TargetExeTextBlock.ToolTip = exePath;
        CopyTargetExeButton.IsEnabled = !string.Equals(exePath, "未設定", StringComparison.Ordinal);

        LastRunTextBlock.Text = _lastRunAt.HasValue ? _lastRunAt.Value.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss") : "未実行";
        ScannedCountTextBlock.Text = _lastScanned.ToString();
        ConvertedCountTextBlock.Text = _lastConverted.ToString();
        ErrorCountTextBlock.Text = _lastErrors.ToString();

        var hasError = !string.IsNullOrWhiteSpace(_lastError);
        LastErrorBorder.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        LastErrorTextBlock.Text = hasError ? _lastError : string.Empty;

        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        var status = _monitor.IsRunning ? "監視中" : "停止中";
        var last = _lastRunAt.HasValue ? _lastRunAt.Value.LocalDateTime.ToString("HH:mm") : "--:--";
        var text = $"{status} 最終:{last} 成功:{_lastConverted} エラー:{_lastErrors}";
        if (text.Length > 63)
        {
            text = text[..63];
        }

        _trayIcon.Text = text;
    }

    private void RefreshLogTail()
    {
        try
        {
            var logDir = _configStore.GetLogDirectory();
            Directory.CreateDirectory(logDir);

            var file = new DirectoryInfo(logDir)
                .GetFiles("app-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (file is null)
            {
                ShowEmptyLogState();
                return;
            }

            var content = ReadLogTailWithRetry(file.FullName, 8);
            if (string.IsNullOrWhiteSpace(content))
            {
                ShowEmptyLogState();
                return;
            }

            LogEmptyStateBorder.Visibility = Visibility.Collapsed;
            LogTailTextBox.Visibility = Visibility.Visible;
            LogTailTextBox.Text = content;
        }
        catch (IOException)
        {
            ShowEmptyLogState();
        }
    }

    private void ShowEmptyLogState()
    {
        LogEmptyStateBorder.Visibility = Visibility.Visible;
        LogTailTextBox.Visibility = Visibility.Collapsed;
        LogTailTextBox.Text = string.Empty;
    }

    private static string ReadLogTailWithRetry(string path, int lineCount)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return ReadLogTail(path, lineCount);
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(60);
            }
        }

        return string.Empty;
    }

    private static string ReadLogTail(string path, int lineCount)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var queue = new Queue<string>(lineCount);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                continue;
            }

            if (queue.Count == lineCount)
            {
                queue.Dequeue();
            }

            queue.Enqueue(line);
        }

        return string.Join(Environment.NewLine, queue).Trim();
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e) => await OpenSettingsInternal();

    private async Task OpenSettingsInternal()
    {
        try
        {
            var window = new SettingsWindow(_config) { Owner = this };
            if (window.ShowDialog() != true || window.SavedConfig is null)
            {
                return;
            }

            _config = Normalize(window.SavedConfig);
            ApplyRuntime(_config.LogLevel);
            ApplyStartup(_config.LaunchOnWindowsStartup);
            await _configStore.SaveAsync(_config);

            if (window.RequestHistoryReset)
            {
                await _historyStore.ResetAsync();
                _converter = new PhotoConversionService(_log, _historyStore);
            }

            if (_monitor.IsRunning)
            {
                _monitor.Start(_config.WatchTargets);
            }

            UpdateStatusUi();
            RefreshLogTail();
        }
        catch (Exception ex)
        {
            UiDiagnostics.LogException("OpenSettingsInternal", ex);
            System.Windows.MessageBox.Show(
                "設定画面の表示中にエラーが発生しました。ログを確認してください。",
                "エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyRuntime(AppLogLevel level)
    {
        if (level == _currentLevel)
        {
            return;
        }

        _currentLevel = level;
        _log = new LogService(_configStore.GetLogDirectory(), level);
        _converter = new PhotoConversionService(_log, _historyStore);
    }

    private void ApplyStartup(bool enabled)
    {
        var startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "VRCJpegAutoGenerator.cmd");
        if (!enabled)
        {
            if (File.Exists(startupPath))
            {
                File.Delete(startupPath);
            }
            return;
        }

        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var content = $"@echo off{Environment.NewLine}start \"\" \"{exe}\" --minimized{Environment.NewLine}";
        File.WriteAllText(startupPath, content, Encoding.ASCII);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsInternal();

    private void OpenLogsInternal()
    {
        var dir = _configStore.GetLogDirectory();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = dir,
            UseShellExecute = true
        });
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e) => OpenOutputFolderInternal();

    private void OpenOutputFolderInternal()
    {
        var output = GetDefaultRule()?.JpegOutputDir;
        if (string.IsNullOrWhiteSpace(output))
        {
            System.Windows.MessageBox.Show("出力フォルダが未設定です。", "未設定", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        Directory.CreateDirectory(output);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = output,
            UseShellExecute = true
        });
    }

    private void CopyLastError_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            System.Windows.Clipboard.SetText(_lastError);
        }
    }

    private void CopyTargetExe_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TargetExeTextBlock.Text) &&
            !string.Equals(TargetExeTextBlock.Text, "未設定", StringComparison.Ordinal))
        {
            System.Windows.Clipboard.SetText(TargetExeTextBlock.Text);
        }
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e) => MinimizeToTray("manual");

    private void MinimizeToTray(string reason)
    {
        _isMinimizingToTray = true;
        try
        {
            ShowInTaskbar = false;
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
            }
            Hide();
        }
        finally
        {
            _isMinimizingToTray = false;
        }

        ShowTrayAnnouncement(reason);
    }

    private void ShowTrayAnnouncement(string reason)
    {
        var shouldShow = reason switch
        {
            "minimize" when !_hasShownMinimizeAnnouncement => true,
            "close" when !_hasShownCloseAnnouncement => true,
            _ => false
        };

        if (!shouldShow)
        {
            return;
        }

        var title = "VRC JPEG Auto Generator";
        var text = reason switch
        {
            "close" => "ウィンドウは終了せず、タスクトレイに格納されました。終了はトレイメニューから行えます。",
            _ => "ウィンドウはタスクトレイに格納されました。ダブルクリックで元に戻せます。"
        };

        try
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = text;
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(3000);
        }
        catch
        {
            // Ignore shell notification failures on environments that suppress balloon tips.
        }

        if (reason == "close")
        {
            _hasShownCloseAnnouncement = true;
        }
        else if (reason == "minimize")
        {
            _hasShownMinimizeAnnouncement = true;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        Close();
    }
}

