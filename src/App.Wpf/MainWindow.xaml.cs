using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Media = System.Windows.Media;
using Core;
using Core.Abstractions;
using Core.Models;
using Infrastructure.Config;
using Infrastructure.Conversion;
using Infrastructure.History;
using Infrastructure.Logging;
using Infrastructure.Monitoring;
using Forms = System.Windows.Forms;

namespace PhotoConverterApp;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ConversionProfile> _profiles = new();
    private readonly ObservableCollection<WatchTarget> _targets = new();
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly IProcessingHistoryStore _historyStore;
    private readonly IGameExitMonitor _monitor = new GameExitMonitor();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly bool _startMinimized;
    private readonly Forms.NotifyIcon _trayIcon;
    private ILogService _log;
    private IPhotoConversionService _converter;
    private AppConfig _config = new();
    private AppLogLevel _currentLevel = AppLogLevel.Information;
    private string? _editingProfileId;
    private bool _applying;
    private bool _allowExit;
    private int _lastScanned;
    private int _lastConverted;
    private int _lastErrors;

    public MainWindow()
    {
        _startMinimized = Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));

        _historyStore = new SqliteProcessingHistoryStore(_configStore.GetDataDirectory());
        _log = new LogService(_configStore.GetLogDirectory(), AppLogLevel.Information);
        _converter = new PhotoConversionService(_log, _historyStore);
        _monitor.GameExited += MonitorOnGameExited;

        _trayIcon = BuildTrayIcon();
        _trayIcon.DoubleClick += TrayIcon_DoubleClick;

        InitializeComponent();
        RunProfileComboBox.ItemsSource = _profiles;
        ProfileEditorComboBox.ItemsSource = _profiles;
        DefaultProfileComboBox.ItemsSource = _profiles;
        TargetProfileComboBox.ItemsSource = _profiles;
        WatchTargetsGrid.ItemsSource = _targets;
        JpegQualitySlider.ValueChanged += (_, _) => JpegQualityValueTextBlock.Text = ((int)JpegQualitySlider.Value).ToString();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _config = Normalize(await _configStore.LoadAsync());
        ApplyRuntime(_config.LogLevel);
        ApplyStartup(_config.LaunchOnWindowsStartup);
        ApplyUi(_config);
        await _configStore.SaveAsync(_config);
        SwitchSection("Dashboard");
        UpdateMonitorState();
        if (_config.MonitorEnabledOnStartup)
        {
            StartMonitor();
        }

        if (_startMinimized)
        {
            MinimizeToTray();
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        _monitor.Stop();
        _config = BuildConfigFromUi();
        await _configStore.SaveAsync(_config);
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _runLock.Dispose();
    }

    private static AppConfig Normalize(AppConfig c)
    {
        c.Profiles ??= new();
        c.WatchTargets ??= new();
        if (c.Profiles.Count == 0)
        {
            var pic = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var id = Guid.NewGuid().ToString("N");
            c.Profiles.Add(new ConversionProfile
            {
                Id = id,
                Name = "VRChat_Default",
                SourceDir = Path.Combine(pic, "VRChat"),
                JpegOutputDir = Path.Combine(pic, "VRChat_jpeg"),
                PngArchiveDir = Path.Combine(pic, "VRChat_png"),
                PngHandlingMode = PngHandlingMode.Move,
                DuplicatePolicy = DuplicatePolicy.Rename,
                IncludeSubdirectories = true,
                JpegQuality = 90,
                RecentFileGuardSeconds = 10
            });
            c.DefaultProfileId = id;
            c.MonitorEnabledOnStartup = true;
            c.LaunchOnWindowsStartup = true;
            c.WatchTargets.Add(new WatchTarget
            {
                Mode = WatchTargetMode.ExeOnly,
                ExeName = @"C:\Program Files (x86)\Steam\steamapps\common\VRChat\VRChat.exe",
                ProfileId = id,
                ProfileName = "VRChat_Default"
            });
        }

        c.DefaultProfileId ??= c.Profiles[0].Id;
        foreach (var t in c.WatchTargets)
        {
            t.Mode = WatchTargetMode.ExeOnly;
            t.AppId = null;
            if (string.IsNullOrWhiteSpace(t.ProfileId))
            {
                t.ProfileId = c.DefaultProfileId;
            }
        }

        return c;
    }

    private void ApplyUi(AppConfig c)
    {
        _applying = true;
        _profiles.Clear();
        foreach (var p in c.Profiles)
        {
            _profiles.Add(Clone(p));
        }

        _targets.Clear();
        foreach (var t in c.WatchTargets)
        {
            _targets.Add(new WatchTarget
            {
                Mode = WatchTargetMode.ExeOnly,
                ExeName = t.ExeName,
                AppId = null,
                ProfileId = t.ProfileId,
                ProfileName = ProfileName(t.ProfileId, c.DefaultProfileId)
            });
        }

        var d = _profiles.First(x => x.Id == c.DefaultProfileId);
        RunProfileComboBox.SelectedItem = d;
        ProfileEditorComboBox.SelectedItem = d;
        DefaultProfileComboBox.SelectedItem = d;
        TargetProfileComboBox.SelectedItem = d;
        _editingProfileId = d.Id;

        LogLevelComboBox.SelectedIndex = LevelToIndex(c.LogLevel);
        MonitorOnStartupCheckBox.IsChecked = c.MonitorEnabledOnStartup;
        LaunchOnWindowsStartupCheckBox.IsChecked = c.LaunchOnWindowsStartup;
        ApplyEditingProfileToUi();
        UpdateLogsPanel();
        _applying = false;
    }

    private AppConfig BuildConfigFromUi()
    {
        SaveEditingProfileFromUi();
        var def = DefaultProfileComboBox.SelectedItem as ConversionProfile ?? _profiles.First();
        return Normalize(new AppConfig
        {
            Profiles = _profiles.Select(Clone).ToList(),
            DefaultProfileId = def.Id,
            WatchTargets = _targets.Select(t => new WatchTarget
            {
                Mode = WatchTargetMode.ExeOnly,
                ExeName = t.ExeName,
                AppId = null,
                ProfileId = t.ProfileId ?? def.Id,
                ProfileName = ProfileName(t.ProfileId ?? def.Id, def.Id)
            }).ToList(),
            LogLevel = IndexToLevel(LogLevelComboBox.SelectedIndex),
            MonitorEnabledOnStartup = MonitorOnStartupCheckBox.IsChecked == true,
            LaunchOnWindowsStartup = LaunchOnWindowsStartupCheckBox.IsChecked == true
        });
    }

    private static ConversionProfile Clone(ConversionProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SourceDir = p.SourceDir,
        JpegOutputDir = p.JpegOutputDir,
        PngArchiveDir = p.PngArchiveDir,
        PngHandlingMode = p.PngHandlingMode,
        JpegQuality = p.JpegQuality,
        IncludeSubdirectories = p.IncludeSubdirectories,
        DuplicatePolicy = p.DuplicatePolicy,
        DryRun = p.DryRun,
        RecentFileGuardSeconds = p.RecentFileGuardSeconds
    };

    private string ProfileName(string? id, string? fallback) => _profiles.FirstOrDefault(x => x.Id == (id ?? fallback))?.Name ?? "(Default)";
    private ConversionProfile EditProfile() => _profiles.First(x => x.Id == _editingProfileId);

    private void SaveEditingProfileFromUi()
    {
        if (_editingProfileId is null || _profiles.Count == 0)
        {
            return;
        }

        var p = EditProfile();
        p.Name = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? "Profile" : ProfileNameTextBox.Text.Trim();
        p.SourceDir = SourceDirTextBox.Text.Trim();
        p.JpegOutputDir = JpegOutputDirTextBox.Text.Trim();
        p.PngArchiveDir = PngArchiveDirTextBox.Text.Trim();
        p.PngHandlingMode = PngModeComboBox.SelectedIndex == 0 ? PngHandlingMode.Move : PngHandlingMode.Copy;
        p.DuplicatePolicy = DuplicatePolicyComboBox.SelectedIndex switch
        {
            1 => DuplicatePolicy.Overwrite,
            2 => DuplicatePolicy.Skip,
            _ => DuplicatePolicy.Rename
        };
        p.JpegQuality = (int)JpegQualitySlider.Value;
        p.IncludeSubdirectories = RecursiveCheckBox.IsChecked == true;
        p.DryRun = DryRunCheckBox.IsChecked == true;
        p.RecentFileGuardSeconds = int.TryParse(RecentGuardTextBox.Text, out var g) ? g : 10;
    }

    private void ApplyEditingProfileToUi()
    {
        if (_editingProfileId is null || _profiles.Count == 0)
        {
            return;
        }

        var p = EditProfile();
        ProfileNameTextBox.Text = p.Name;
        SourceDirTextBox.Text = p.SourceDir;
        JpegOutputDirTextBox.Text = p.JpegOutputDir;
        PngArchiveDirTextBox.Text = p.PngArchiveDir;
        PngModeComboBox.SelectedIndex = p.PngHandlingMode == PngHandlingMode.Move ? 0 : 1;
        DuplicatePolicyComboBox.SelectedIndex = p.DuplicatePolicy switch
        {
            DuplicatePolicy.Rename => 0,
            DuplicatePolicy.Overwrite => 1,
            _ => 2
        };
        JpegQualitySlider.Value = p.JpegQuality;
        RecentGuardTextBox.Text = p.RecentFileGuardSeconds.ToString();
        RecursiveCheckBox.IsChecked = p.IncludeSubdirectories;
        DryRunCheckBox.IsChecked = p.DryRun;
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
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "GamePhotoAutoConverter.cmd");
        if (!enabled)
        {
            if (File.Exists(p))
            {
                File.Delete(p);
            }
            return;
        }

        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var lines = $"@echo off{Environment.NewLine}start \"\" \"{exe}\" --minimized{Environment.NewLine}";
        File.WriteAllText(p, lines, Encoding.ASCII);
    }

    private async Task RunProfilesAsync(string trigger, IReadOnlyList<ConversionProfile> list)
    {
        if (!await _runLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            ProgressBar.IsIndeterminate = true;
            SummaryTextBox.Clear();
            int scanned = 0;
            int converted = 0;
            int errors = 0;

            foreach (var p in list)
            {
                var cfg = new AppConfig
                {
                    SourceDir = p.SourceDir,
                    JpegOutputDir = p.JpegOutputDir,
                    PngArchiveDir = p.PngArchiveDir,
                    PngHandlingMode = p.PngHandlingMode,
                    JpegQuality = p.JpegQuality,
                    IncludeSubdirectories = p.IncludeSubdirectories,
                    DuplicatePolicy = p.DuplicatePolicy,
                    DryRun = p.DryRun,
                    RecentFileGuardSeconds = p.RecentFileGuardSeconds,
                    LogLevel = _config.LogLevel
                };

                var progress = new Progress<ConversionProgress>(x =>
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Maximum = x.TotalFiles == 0 ? 1 : x.TotalFiles;
                    ProgressBar.Value = Math.Min(x.ProcessedFiles, ProgressBar.Maximum);
                    CurrentFileTextBlock.Text = $"[{p.Name}] {Path.GetFileName(x.CurrentFile)}";
                });

                var r = await _converter.RunAsync(cfg, trigger, progress);
                scanned += r.ScannedCount;
                converted += r.ConvertedCount;
                errors += r.ErrorCount;
                SummaryTextBox.AppendText($"[{p.Name}] scanned={r.ScannedCount} converted={r.ConvertedCount} errors={r.ErrorCount}{Environment.NewLine}");
            }

            _lastScanned = scanned;
            _lastConverted = converted;
            _lastErrors = errors;
            UpdateStatCards();
            StatusTextBlock.Text = "処理完了";
            CurrentFileTextBlock.Text = "待機中";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "処理エラー";
            SummaryTextBox.Text = ex.ToString();
        }
        finally
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            _runLock.Release();
        }
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        if (RunProfileComboBox.SelectedItem is ConversionProfile p)
        {
            await RunProfilesAsync("manual", new[] { p });
        }
    }

    private void MonitorOnGameExited(object? sender, string processName)
    {
        _ = Dispatcher.InvokeAsync(() => HandleGameExitedAsync(processName));
    }

    private async Task HandleGameExitedAsync(string processName)
    {
        try
        {
            var profiles = _config.WatchTargets
                .Where(t => string.Equals(Path.GetFileName(t.ExeName), Path.GetFileName(processName), StringComparison.OrdinalIgnoreCase))
                .Select(t => _config.Profiles.FirstOrDefault(p => p.Id == (t.ProfileId ?? _config.DefaultProfileId)))
                .Where(p => p is not null)
                .Cast<ConversionProfile>()
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToList();

            if (profiles.Count == 0)
            {
                return;
            }

            var delay = profiles.Max(p => p.RecentFileGuardSeconds) + 1;
            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            await RunProfilesAsync($"game_exit:{processName}", profiles);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "監視トリガーエラー";
            SummaryTextBox.Text = ex.ToString();
        }
    }

    private void StartMonitor_Click(object sender, RoutedEventArgs e) => StartMonitor();

    private void StartMonitor()
    {
        _config = BuildConfigFromUi();
        _monitor.Start(_config.WatchTargets);
        UpdateMonitorState();
    }

    private void StopMonitor_Click(object sender, RoutedEventArgs e)
    {
        _monitor.Stop();
        UpdateMonitorState();
    }

    private void UpdateMonitorState()
    {
        StatusTextBlock.Text = _monitor.IsRunning ? "監視中" : "停止";
        StatusIndicatorEllipse.Fill = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(_monitor.IsRunning ? "#4CAF50" : "#DC2626"));
        StatusIndicatorTextBlock.Text = _monitor.IsRunning ? "稼働中" : "停止";
    }

    private void UpdateStatCards()
    {
        ScannedCountTextBlock.Text = _lastScanned.ToString();
        ConvertedCountTextBlock.Text = _lastConverted.ToString();
        ErrorCountTextBlock.Text = _lastErrors.ToString();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _config = BuildConfigFromUi();
        ApplyRuntime(_config.LogLevel);
        ApplyStartup(_config.LaunchOnWindowsStartup);
        await _configStore.SaveAsync(_config);
        ToastTextBlock.Text = "保存完了";
        ToastBorder.Visibility = Visibility.Visible;
        await Task.Delay(1500);
        ToastBorder.Visibility = Visibility.Collapsed;
        UpdateLogsPanel();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveEditingProfileFromUi();
        var p = new ConversionProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Profile_{_profiles.Count + 1}",
            IncludeSubdirectories = true,
            JpegQuality = 90,
            RecentFileGuardSeconds = 10
        };
        _profiles.Add(p);
        _editingProfileId = p.Id;
        ProfileEditorComboBox.SelectedItem = p;
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1 || _editingProfileId is null)
        {
            return;
        }

        var p = EditProfile();
        _profiles.Remove(p);
        _editingProfileId = _profiles[0].Id;
        ProfileEditorComboBox.SelectedItem = _profiles[0];
        ApplyEditingProfileToUi();
    }

    private void ProfileEditorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying)
        {
            return;
        }

        SaveEditingProfileFromUi();
        if (ProfileEditorComboBox.SelectedItem is ConversionProfile p)
        {
            _editingProfileId = p.Id;
            ApplyEditingProfileToUi();
        }
    }

    private void ProfileNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_applying)
        {
            SaveEditingProfileFromUi();
        }
    }

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetExeNameTextBox.Text))
        {
            return;
        }

        var p = TargetProfileComboBox.SelectedItem as ConversionProfile ?? _profiles.First();
        _targets.Add(new WatchTarget
        {
            Mode = WatchTargetMode.ExeOnly,
            ExeName = TargetExeNameTextBox.Text.Trim(),
            AppId = null,
            ProfileId = p.Id,
            ProfileName = p.Name
        });
    }

    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (WatchTargetsGrid.SelectedItem is WatchTarget t)
        {
            _targets.Remove(t);
        }
    }

    private static string? BrowseFolder(string current)
    {
        using var d = new Forms.FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(current) ? current : string.Empty,
            ShowNewFolderButton = true
        };
        return d.ShowDialog() == Forms.DialogResult.OK ? d.SelectedPath : null;
    }

    private void BrowseSourceDir_Click(object sender, RoutedEventArgs e)
    {
        var p = BrowseFolder(SourceDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(p))
        {
            SourceDirTextBox.Text = p;
        }
    }

    private void BrowseJpegOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var p = BrowseFolder(JpegOutputDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(p))
        {
            JpegOutputDirTextBox.Text = p;
        }
    }

    private void BrowsePngArchiveDir_Click(object sender, RoutedEventArgs e)
    {
        var p = BrowseFolder(PngArchiveDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(p))
        {
            PngArchiveDirTextBox.Text = p;
        }
    }

    private void PathTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PathTextBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] p && p.Length > 0)
        {
            tb.Text = Directory.Exists(p[0]) ? p[0] : (Path.GetDirectoryName(p[0]) ?? tb.Text);
        }
    }

    private async void ResetHistoryDb_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("履歴DBをリセットします。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await _historyStore.ResetAsync();
        _converter = new PhotoConversionService(_log, _historyStore);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var p = _configStore.GetLogDirectory();
        Directory.CreateDirectory(p);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = p,
            UseShellExecute = true
        });
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var p = (RunProfileComboBox.SelectedItem as ConversionProfile)?.JpegOutputDir;
        if (!string.IsNullOrWhiteSpace(p))
        {
            Directory.CreateDirectory(p);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = p,
                UseShellExecute = true
            });
        }
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SwitchSection("Dashboard");
    private void NavProfiles_Click(object sender, RoutedEventArgs e) => SwitchSection("Profiles");
    private void NavLogs_Click(object sender, RoutedEventArgs e) => SwitchSection("Logs");

    private void SwitchSection(string s)
    {
        DashboardView.Visibility = s == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesView.Visibility = s == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        LogsView.Visibility = s == "Logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTitleTextBlock.Text = s;
    }

    private static int LevelToIndex(AppLogLevel l) => l switch
    {
        AppLogLevel.Error => 0,
        AppLogLevel.Warning => 1,
        AppLogLevel.Debug => 3,
        _ => 2
    };

    private static AppLogLevel IndexToLevel(int i) => i switch
    {
        0 => AppLogLevel.Error,
        1 => AppLogLevel.Warning,
        3 => AppLogLevel.Debug,
        _ => AppLogLevel.Information
    };

    private void UpdateLogsPanel()
    {
        LogsInfoTextBox.Text =
            $"Log Directory: {_configStore.GetLogDirectory()}{Environment.NewLine}" +
            $"Watch Targets: {_targets.Count}{Environment.NewLine}" +
            $"Last Summary:{Environment.NewLine}{SummaryTextBox.Text}";
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        return new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Game Photo Auto Converter",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new System.Drawing.Icon(iconPath);
            }
            catch
            {
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void TrayIcon_DoubleClick(object? sender, EventArgs e) => RestoreFromTray();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && IsVisible)
        {
            MinimizeToTray();
        }
    }

    private void MinimizeToTray()
    {
        ShowInTaskbar = false;
        if (WindowState != WindowState.Minimized)
        {
            WindowState = WindowState.Minimized;
        }
        Hide();
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


