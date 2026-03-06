using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Core.Abstractions;
using Core.Models;
using Infrastructure.Config;
using Forms = System.Windows.Forms;

namespace PhotoConverterApp;

public partial class SettingsWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly AppConfig _original;
    private readonly AppConfig _draft;
    private bool _isApplying;
    private bool _isDirty;
    private bool _closingByCode;
    private bool _uiReady;

    public AppConfig? SavedConfig { get; private set; }
    public bool RequestHistoryReset { get; private set; }

    public SettingsWindow(AppConfig initial)
    {
        InitializeComponent();
        _original = CloneConfig(initial);
        _draft = CloneConfig(initial);
        EnsureDefaultRule(_draft);

        LoadUiFromDraft();
        _uiReady = true;
        ValidateAndUpdateUi(showErrors: false);
    }

    private static AppConfig CloneConfig(AppConfig source)
    {
        return new AppConfig
        {
            SourceDir = source.SourceDir,
            JpegOutputDir = source.JpegOutputDir,
            PngArchiveDir = source.PngArchiveDir,
            PngHandlingMode = source.PngHandlingMode,
            JpegQuality = source.JpegQuality,
            IncludeSubdirectories = source.IncludeSubdirectories,
            DuplicatePolicy = source.DuplicatePolicy,
            DryRun = source.DryRun,
            LogLevel = source.LogLevel,
            LaunchOnWindowsStartup = source.LaunchOnWindowsStartup,
            RecentFileGuardSeconds = source.RecentFileGuardSeconds,
            MonitorEnabledOnStartup = source.MonitorEnabledOnStartup,
            DefaultProfileId = source.DefaultProfileId,
            Profiles = source.Profiles.Select(CloneRule).ToList(),
            WatchTargets = source.WatchTargets.Select(t => new WatchTarget
            {
                Mode = t.Mode,
                ExeName = t.ExeName,
                AppId = t.AppId,
                ProfileId = t.ProfileId,
                ProfileName = t.ProfileName
            }).ToList()
        };
    }

    private static ConversionProfile CloneRule(ConversionProfile source)
    {
        return new ConversionProfile
        {
            Id = source.Id,
            Name = source.Name,
            SourceDir = source.SourceDir,
            JpegOutputDir = source.JpegOutputDir,
            PngArchiveDir = source.PngArchiveDir,
            PngHandlingMode = source.PngHandlingMode,
            JpegQuality = source.JpegQuality,
            IncludeSubdirectories = source.IncludeSubdirectories,
            DuplicatePolicy = source.DuplicatePolicy,
            DryRun = source.DryRun,
            RecentFileGuardSeconds = source.RecentFileGuardSeconds
        };
    }

    private static void EnsureDefaultRule(AppConfig config)
    {
        config.Profiles ??= new List<ConversionProfile>();
        config.WatchTargets ??= new List<WatchTarget>();

        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(new ConversionProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "VRChat_Default",
                JpegQuality = 90,
                IncludeSubdirectories = true,
                RecentFileGuardSeconds = 10
            });
            config.LaunchOnWindowsStartup = true;
        }

        config.DefaultProfileId ??= config.Profiles[0].Id;
    }

    private ConversionProfile GetDefaultRule()
    {
        return _draft.Profiles.FirstOrDefault(p => p.Id == _draft.DefaultProfileId) ?? _draft.Profiles[0];
    }

    private void LoadUiFromDraft()
    {
        _isApplying = true;
        try
        {
            var rule = GetDefaultRule();

            RuleNameTextBox.Text = rule.Name;
            SourceDirTextBox.Text = rule.SourceDir;
            JpegOutputDirTextBox.Text = rule.JpegOutputDir;
            PngArchiveDirTextBox.Text = rule.PngArchiveDir;
            IncludeSubdirectoriesCheckBox.IsChecked = rule.IncludeSubdirectories;
            MonitorOnStartupCheckBox.IsChecked = _draft.MonitorEnabledOnStartup;
            JpegQualitySlider.Value = Math.Clamp(rule.JpegQuality, 1, 100);
            JpegQualityValueTextBlock.Text = ((int)JpegQualitySlider.Value).ToString();

            PngModeComboBox.SelectedIndex = rule.PngHandlingMode switch
            {
                PngHandlingMode.Copy => 1,
                PngHandlingMode.Delete => 2,
                _ => 0
            };
            UpdatePngArchiveEnabledState();
            DuplicatePolicyComboBox.SelectedIndex = rule.DuplicatePolicy switch
            {
                DuplicatePolicy.Overwrite => 1,
                DuplicatePolicy.Skip => 2,
                _ => 0
            };
            RecentGuardTextBox.Text = rule.RecentFileGuardSeconds.ToString();
            DryRunCheckBox.IsChecked = rule.DryRun;
            LaunchOnWindowsStartupCheckBox.IsChecked = _draft.LaunchOnWindowsStartup;
            LogLevelComboBox.SelectedIndex = _draft.LogLevel switch
            {
                AppLogLevel.Error => 0,
                AppLogLevel.Warning => 1,
                AppLogLevel.Debug => 3,
                _ => 2
            };

            var watch = _draft.WatchTargets.FirstOrDefault(t => t.ProfileId == rule.Id) ?? _draft.WatchTargets.FirstOrDefault();
            WatchExeNameTextBox.Text = watch?.ExeName ?? string.Empty;
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void InputChangedCore()
    {
        if (_isApplying || !_uiReady)
        {
            return;
        }

        _isDirty = true;
        ValidateAndUpdateUi(showErrors: false);
    }

    private void TextInputChanged(object? sender, TextChangedEventArgs e)
    {
        InputChangedCore();
    }

    private void SelectionInputChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, PngModeComboBox))
        {
            UpdatePngArchiveEnabledState();
        }

        InputChangedCore();
    }

    private void ToggleInputChanged(object? sender, RoutedEventArgs e)
    {
        InputChangedCore();
    }

    private void JpegQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JpegQualityValueTextBlock is not null && JpegQualitySlider is not null)
        {
            JpegQualityValueTextBlock.Text = ((int)JpegQualitySlider.Value).ToString();
        }
        InputChangedCore();
    }

    private bool ValidateAndUpdateUi(bool showErrors)
    {
        if (!_uiReady || SaveButton is null || DirtyStateTextBlock is null || ValidationMessageTextBlock is null)
        {
            return false;
        }

        List<string> errors;
        try
        {
            errors = ValidateInputs();
        }
        catch (Exception ex)
        {
            UiDiagnostics.LogException("Settings.ValidateAndUpdateUi", ex);
            errors = new List<string> { "・入力値の検証中にエラーが発生しました。値を見直してください。" };
        }

        SaveButton.IsEnabled = _isDirty && errors.Count == 0;
        DirtyStateTextBlock.Text = _isDirty ? "未保存の変更があります" : "変更はありません";
        Title = _isDirty ? "設定 *" : "設定";
        ValidationMessageTextBlock.Text = showErrors || _isDirty ? string.Join(Environment.NewLine, errors) : string.Empty;
        return errors.Count == 0;
    }

    private List<string> ValidateInputs()
    {
        var errors = new List<string>();

        var source = SourceDirTextBox?.Text?.Trim() ?? string.Empty;
        var jpegOut = JpegOutputDirTextBox?.Text?.Trim() ?? string.Empty;
        var pngArchive = PngArchiveDirTextBox?.Text?.Trim() ?? string.Empty;
        var exeName = WatchExeNameTextBox?.Text?.Trim() ?? string.Empty;
        var pngMode = PngModeComboBox?.SelectedIndex ?? 0;
        var isPngDeleteMode = pngMode == 2;

        if (string.IsNullOrWhiteSpace(RuleNameTextBox?.Text))
        {
            errors.Add("・ルール名を入力してください。");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            errors.Add("・入力フォルダを指定してください。");
        }
        else if (!Directory.Exists(source))
        {
            errors.Add("・入力フォルダが存在しません。");
        }

        if (string.IsNullOrWhiteSpace(jpegOut))
        {
            errors.Add("・JPEG出力先を指定してください。");
        }

        if (!isPngDeleteMode && string.IsNullOrWhiteSpace(pngArchive))
        {
            errors.Add("・PNG保管先を指定してください。");
        }

        if (string.IsNullOrWhiteSpace(exeName))
        {
            errors.Add("・監視対象EXEを指定してください。");
        }

        if (!int.TryParse(RecentGuardTextBox?.Text?.Trim(), out var guard) || guard is < 0 or > 600)
        {
            errors.Add("・除外秒数は0〜600の整数で入力してください。");
        }

        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(jpegOut))
        {
            if (IsSameOrNested(source, jpegOut) || IsSameOrNested(jpegOut, source))
            {
                errors.Add("・入力フォルダとJPEG出力先を同一または内包関係にできません。");
            }
        }

        if (!isPngDeleteMode && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(pngArchive))
        {
            if (IsSameOrNested(source, pngArchive) || IsSameOrNested(pngArchive, source))
            {
                errors.Add("・入力フォルダとPNG保管先を同一または内包関係にできません。");
            }
        }

        if (!isPngDeleteMode && !string.IsNullOrWhiteSpace(jpegOut) && !string.IsNullOrWhiteSpace(pngArchive))
        {
            if (IsSameOrNested(jpegOut, pngArchive) || IsSameOrNested(pngArchive, jpegOut))
            {
                errors.Add("・JPEG出力先とPNG保管先を同一または内包関係にできません。");
            }
        }

        return errors;
    }

    private static bool IsSameOrNested(string a, string b)
    {
        var normA = TryNormalizePath(a);
        var normB = TryNormalizePath(b);
        if (normA is null || normB is null)
        {
            return false;
        }

        return normA.Equals(normB, StringComparison.OrdinalIgnoreCase) || normB.StartsWith(normA, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryNormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch
        {
            return null;
        }
    }

    private bool ApplyUiToDraft()
    {
        if (!ValidateAndUpdateUi(showErrors: true))
        {
            return false;
        }

        var rule = GetDefaultRule();
        rule.Name = RuleNameTextBox.Text.Trim();
        rule.SourceDir = SourceDirTextBox.Text.Trim();
        rule.JpegOutputDir = JpegOutputDirTextBox.Text.Trim();
        rule.PngArchiveDir = PngArchiveDirTextBox.Text.Trim();
        rule.IncludeSubdirectories = IncludeSubdirectoriesCheckBox.IsChecked == true;
        rule.JpegQuality = (int)JpegQualitySlider.Value;
        rule.PngHandlingMode = PngModeComboBox.SelectedIndex switch
        {
            1 => PngHandlingMode.Copy,
            2 => PngHandlingMode.Delete,
            _ => PngHandlingMode.Move
        };
        rule.DuplicatePolicy = DuplicatePolicyComboBox.SelectedIndex switch
        {
            1 => DuplicatePolicy.Overwrite,
            2 => DuplicatePolicy.Skip,
            _ => DuplicatePolicy.Rename
        };
        rule.RecentFileGuardSeconds = int.Parse(RecentGuardTextBox.Text.Trim());
        rule.DryRun = DryRunCheckBox.IsChecked == true;

        _draft.LogLevel = LogLevelComboBox.SelectedIndex switch
        {
            0 => AppLogLevel.Error,
            1 => AppLogLevel.Warning,
            3 => AppLogLevel.Debug,
            _ => AppLogLevel.Information
        };

        _draft.MonitorEnabledOnStartup = MonitorOnStartupCheckBox.IsChecked == true;
        _draft.LaunchOnWindowsStartup = LaunchOnWindowsStartupCheckBox.IsChecked == true;
        _draft.DefaultProfileId = rule.Id;

        var exeName = WatchExeNameTextBox.Text.Trim();
        var watch = _draft.WatchTargets.FirstOrDefault(t => t.ProfileId == rule.Id) ?? _draft.WatchTargets.FirstOrDefault();
        if (watch is null)
        {
            _draft.WatchTargets.Add(new WatchTarget
            {
                Mode = WatchTargetMode.ExeOnly,
                ExeName = exeName,
                AppId = null,
                ProfileId = rule.Id,
                ProfileName = rule.Name
            });
        }
        else
        {
            watch.Mode = WatchTargetMode.ExeOnly;
            watch.ExeName = exeName;
            watch.AppId = null;
            watch.ProfileId = rule.Id;
            watch.ProfileName = rule.Name;
        }

        if (CreateMissingFoldersCheckBox.IsChecked == true)
        {
            Directory.CreateDirectory(rule.JpegOutputDir);
            if (rule.PngHandlingMode != PngHandlingMode.Delete)
            {
                Directory.CreateDirectory(rule.PngArchiveDir);
            }
        }

        return true;
    }

    private void UpdatePngArchiveEnabledState()
    {
        var enabled = (PngModeComboBox?.SelectedIndex ?? 0) != 2;
        if (PngArchiveDirTextBox is not null)
        {
            PngArchiveDirTextBox.IsEnabled = enabled;
        }
        if (BrowsePngArchiveDirButton is not null)
        {
            BrowsePngArchiveDirButton.IsEnabled = enabled;
        }
        if (PngArchiveLabelTextBlock is not null)
        {
            PngArchiveLabelTextBlock.Opacity = enabled ? 1.0 : 0.6;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyUiToDraft())
        {
            return;
        }

        SavedConfig = CloneConfig(_draft);
        _closingByCode = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _closingByCode = true;
        DialogResult = false;
        Close();
    }

    private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingByCode || !_isDirty)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "未保存の変更があります。保存しますか？",
            "確認",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == System.Windows.MessageBoxResult.No)
        {
            return;
        }

        if (!ApplyUiToDraft())
        {
            e.Cancel = true;
            return;
        }

        SavedConfig = CloneConfig(_draft);
        DialogResult = true;
    }

    private void ResetHistoryDb_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "履歴DBをリセットします。重複判定履歴が消えます。実行しますか？",
            "確認",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            RequestHistoryReset = true;
            _isDirty = true;
            ValidateAndUpdateUi(showErrors: false);
        }
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "設定フォルダを開きます。よろしいですか？",
            "確認",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var path = _configStore.GetDataDirectory();
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "ログフォルダを開きます。よろしいですか？",
            "確認",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var path = _configStore.GetLogDirectory();
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }

    private void BrowseSourceDir_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseFolder(SourceDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourceDirTextBox.Text = path;
        }
    }

    private void BrowseJpegOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseFolder(JpegOutputDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            JpegOutputDirTextBox.Text = path;
        }
    }

    private void BrowsePngArchiveDir_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseFolder(PngArchiveDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            PngArchiveDirTextBox.Text = path;
        }
    }

    private static string? BrowseFolder(string current)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(current) ? current : string.Empty,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}



