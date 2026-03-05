using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Core.Models;
using Forms = System.Windows.Forms;

namespace PhotoConverterApp;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<ConversionProfile> _profiles = new();
    private readonly ObservableCollection<WatchTarget> _targets = new();
    private string? _selectedProfileId;
    private bool _isApplyingUi;

    public AppConfig ResultConfig { get; private set; } = new();
    public bool RequestHistoryReset { get; private set; }

    public SettingsWindow(AppConfig initialConfig)
    {
        InitializeComponent();
        WatchTargetsGrid.ItemsSource = _targets;
        ProfileComboBox.ItemsSource = _profiles;
        DefaultProfileComboBox.ItemsSource = _profiles;
        TargetProfileComboBox.ItemsSource = _profiles;
        ApplyConfig(initialConfig);
    }

    private static ConversionProfile CloneProfile(ConversionProfile src)
    {
        return new ConversionProfile
        {
            Id = src.Id,
            Name = src.Name,
            SourceDir = src.SourceDir,
            JpegOutputDir = src.JpegOutputDir,
            PngArchiveDir = src.PngArchiveDir,
            PngHandlingMode = src.PngHandlingMode,
            JpegQuality = src.JpegQuality,
            IncludeSubdirectories = src.IncludeSubdirectories,
            DuplicatePolicy = src.DuplicatePolicy,
            DryRun = src.DryRun,
            RecentFileGuardSeconds = src.RecentFileGuardSeconds
        };
    }

    private void ApplyConfig(AppConfig config)
    {
        _isApplyingUi = true;
        try
        {
            _profiles.Clear();
            foreach (var p in config.Profiles)
            {
                _profiles.Add(CloneProfile(p));
            }

            _targets.Clear();
            foreach (var t in config.WatchTargets)
            {
                _targets.Add(new WatchTarget
                {
                    Mode = t.Mode,
                    ExeName = t.ExeName,
                    AppId = t.AppId,
                    ProfileId = t.ProfileId,
                    ProfileName = ResolveProfileName(t.ProfileId)
                });
            }

            _selectedProfileId = _profiles.FirstOrDefault()?.Id;
            ProfileComboBox.SelectedItem = _profiles.FirstOrDefault(x => x.Id == _selectedProfileId);
            DefaultProfileComboBox.SelectedItem = _profiles.FirstOrDefault(x => x.Id == config.DefaultProfileId) ?? _profiles.FirstOrDefault();
            TargetProfileComboBox.SelectedItem = DefaultProfileComboBox.SelectedItem;
            LogLevelComboBox.SelectedIndex = LogLevelToSelectedIndex(config.LogLevel);
            MonitorOnStartupCheckBox.IsChecked = config.MonitorEnabledOnStartup;
            LaunchOnWindowsStartupCheckBox.IsChecked = config.LaunchOnWindowsStartup;
            ApplySelectedProfileToUi();
        }
        finally
        {
            _isApplyingUi = false;
        }
    }

    private void SaveSelectedProfileFromUi()
    {
        var profile = GetSelectedProfile();
        if (profile is null)
        {
            return;
        }

        if (!int.TryParse(JpegQualityTextBox.Text, out var quality))
        {
            quality = 90;
        }

        if (!int.TryParse(RecentGuardTextBox.Text, out var guard))
        {
            guard = 10;
        }

        profile.Name = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? "Profile" : ProfileNameTextBox.Text.Trim();
        profile.SourceDir = SourceDirTextBox.Text.Trim();
        profile.JpegOutputDir = JpegOutputDirTextBox.Text.Trim();
        profile.PngArchiveDir = PngArchiveDirTextBox.Text.Trim();
        profile.PngHandlingMode = PngModeComboBox.SelectedIndex == 0 ? PngHandlingMode.Move : PngHandlingMode.Copy;
        profile.DuplicatePolicy = DuplicatePolicyComboBox.SelectedIndex switch
        {
            1 => DuplicatePolicy.Overwrite,
            2 => DuplicatePolicy.Skip,
            _ => DuplicatePolicy.Rename
        };
        profile.JpegQuality = quality;
        profile.IncludeSubdirectories = RecursiveCheckBox.IsChecked == true;
        profile.DryRun = DryRunCheckBox.IsChecked == true;
        profile.RecentFileGuardSeconds = guard;
        RefreshProfileBindings();
    }

    private void ApplySelectedProfileToUi()
    {
        var profile = GetSelectedProfile();
        if (profile is null)
        {
            return;
        }

        ProfileNameTextBox.Text = profile.Name;
        SourceDirTextBox.Text = profile.SourceDir;
        JpegOutputDirTextBox.Text = profile.JpegOutputDir;
        PngArchiveDirTextBox.Text = profile.PngArchiveDir;
        PngModeComboBox.SelectedIndex = profile.PngHandlingMode == PngHandlingMode.Move ? 0 : 1;
        DuplicatePolicyComboBox.SelectedIndex = profile.DuplicatePolicy switch
        {
            DuplicatePolicy.Rename => 0,
            DuplicatePolicy.Overwrite => 1,
            _ => 2
        };
        JpegQualityTextBox.Text = profile.JpegQuality.ToString();
        RecentGuardTextBox.Text = profile.RecentFileGuardSeconds.ToString();
        RecursiveCheckBox.IsChecked = profile.IncludeSubdirectories;
        DryRunCheckBox.IsChecked = profile.DryRun;
    }

    private ConversionProfile? GetSelectedProfile()
    {
        return _profiles.FirstOrDefault(x => x.Id == _selectedProfileId) ?? _profiles.FirstOrDefault();
    }

    private string ResolveProfileName(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return "(Default)";
        }

        return _profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? profileId;
    }

    private void RefreshProfileBindings()
    {
        ProfileComboBox.Items.Refresh();
        DefaultProfileComboBox.Items.Refresh();
        TargetProfileComboBox.Items.Refresh();
        foreach (var target in _targets)
        {
            target.ProfileName = ResolveProfileName(target.ProfileId);
        }

        WatchTargetsGrid.Items.Refresh();
    }

    private AppConfig BuildConfig()
    {
        SaveSelectedProfileFromUi();
        var defaultProfile = DefaultProfileComboBox.SelectedItem as ConversionProfile ?? _profiles.FirstOrDefault();

        return new AppConfig
        {
            LogLevel = LogLevelFromSelectedIndex(LogLevelComboBox.SelectedIndex),
            MonitorEnabledOnStartup = MonitorOnStartupCheckBox.IsChecked == true,
            LaunchOnWindowsStartup = LaunchOnWindowsStartupCheckBox.IsChecked == true,
            DefaultProfileId = defaultProfile?.Id,
            Profiles = _profiles.Select(CloneProfile).ToList(),
            WatchTargets = _targets.Select(t => new WatchTarget
            {
                Mode = t.Mode,
                ExeName = t.ExeName,
                AppId = t.AppId,
                ProfileId = t.ProfileId ?? defaultProfile?.Id,
                ProfileName = ResolveProfileName(t.ProfileId ?? defaultProfile?.Id)
            }).ToList()
        };
    }

    private static int LogLevelToSelectedIndex(AppLogLevel level) =>
        level switch
        {
            AppLogLevel.Error => 0,
            AppLogLevel.Warning => 1,
            AppLogLevel.Debug => 3,
            _ => 2
        };

    private static AppLogLevel LogLevelFromSelectedIndex(int selectedIndex) =>
        selectedIndex switch
        {
            0 => AppLogLevel.Error,
            1 => AppLogLevel.Warning,
            3 => AppLogLevel.Debug,
            _ => AppLogLevel.Information
        };

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedProfileFromUi();
        var profile = new ConversionProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Profile {_profiles.Count + 1}"
        };
        _profiles.Add(profile);
        _selectedProfileId = profile.Id;
        ProfileComboBox.SelectedItem = profile;
        if (DefaultProfileComboBox.SelectedItem is null)
        {
            DefaultProfileComboBox.SelectedItem = profile;
        }

        ApplySelectedProfileToUi();
        RefreshProfileBindings();
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedProfile();
        if (selected is null || _profiles.Count <= 1)
        {
            return;
        }

        var removedId = selected.Id;
        _profiles.Remove(selected);
        var fallback = _profiles[0];
        _selectedProfileId = fallback.Id;

        foreach (var target in _targets.Where(t => string.Equals(t.ProfileId, removedId, StringComparison.OrdinalIgnoreCase)))
        {
            target.ProfileId = fallback.Id;
            target.ProfileName = fallback.Name;
        }

        if (DefaultProfileComboBox.SelectedItem is ConversionProfile def && string.Equals(def.Id, removedId, StringComparison.OrdinalIgnoreCase))
        {
            DefaultProfileComboBox.SelectedItem = fallback;
        }

        ProfileComboBox.SelectedItem = fallback;
        ApplySelectedProfileToUi();
        RefreshProfileBindings();
    }

    private void ProfileComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingUi)
        {
            return;
        }

        SaveSelectedProfileFromUi();
        if (ProfileComboBox.SelectedItem is ConversionProfile profile)
        {
            _selectedProfileId = profile.Id;
            ApplySelectedProfileToUi();
        }
    }

    private void ProfileNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isApplyingUi)
        {
            return;
        }

        SaveSelectedProfileFromUi();
    }

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        var exe = TargetExeNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(exe))
        {
            System.Windows.MessageBox.Show("exe名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int? appId = null;
        if (int.TryParse(TargetAppIdTextBox.Text.Trim(), out var parsed))
        {
            appId = parsed;
        }

        var profile = TargetProfileComboBox.SelectedItem as ConversionProfile ??
                      DefaultProfileComboBox.SelectedItem as ConversionProfile ??
                      _profiles.First();

        _targets.Add(new WatchTarget
        {
            Mode = TargetModeComboBox.SelectedIndex == 1 ? WatchTargetMode.Steam : WatchTargetMode.ExeOnly,
            ExeName = exe,
            AppId = appId,
            ProfileId = profile.Id,
            ProfileName = profile.Name
        });

        TargetExeNameTextBox.Clear();
        TargetAppIdTextBox.Clear();
    }

    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (WatchTargetsGrid.SelectedItem is WatchTarget target)
        {
            _targets.Remove(target);
        }
    }

    private void BrowseSourceDir_Click(object sender, RoutedEventArgs e)
    {
        var result = BrowseFolder(SourceDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(result))
        {
            SourceDirTextBox.Text = result;
        }
    }

    private void BrowseJpegOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var result = BrowseFolder(JpegOutputDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(result))
        {
            JpegOutputDirTextBox.Text = result;
        }
    }

    private void BrowsePngArchiveDir_Click(object sender, RoutedEventArgs e)
    {
        var result = BrowseFolder(PngArchiveDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(result))
        {
            PngArchiveDirTextBox.Text = result;
        }
    }

    private static string? BrowseFolder(string currentPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : string.Empty,
            ShowNewFolderButton = true
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void ResetHistoryDbRequest_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "保存時に履歴DBリセットを実行します。続行しますか？",
            "履歴DBリセット確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            RequestHistoryReset = true;
            StatusTextBlock.Text = "履歴DBリセット要求: ON";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultConfig = BuildConfig();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
