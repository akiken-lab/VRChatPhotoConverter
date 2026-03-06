using System.IO;
using System.Windows;
using System.Windows.Controls;
using Core.Models;
using Forms = System.Windows.Forms;

namespace PhotoConverterApp;

public partial class FirstRunWizardWindow : Window
{
    private int _step = 1;

    public AppConfig? CreatedConfig { get; private set; }
    public bool StartMonitoringAfterFinish { get; private set; }

    public FirstRunWizardWindow()
    {
        InitializeComponent();

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        SourceDirTextBox.Text = Path.Combine(pictures, "VRChat");
        JpegDirTextBox.Text = Path.Combine(pictures, "VRChat_jpeg");
        PngDirTextBox.Text = Path.Combine(pictures, "VRChat_png");

        RefreshStepUi();
        UpdatePngFolderEnabledState();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= 1)
        {
            return;
        }

        _step--;
        RefreshStepUi();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;

        if (_step == 2)
        {
            if (string.IsNullOrWhiteSpace(SourceDirTextBox.Text))
            {
                ValidationTextBlock.Text = "入力フォルダを指定してください。";
                return;
            }

            if (string.IsNullOrWhiteSpace(JpegDirTextBox.Text))
            {
                var source = SourceDirTextBox.Text.Trim();
                var parent = Directory.Exists(source) ? Directory.GetParent(source)?.FullName : null;
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    JpegDirTextBox.Text = Path.Combine(parent, "VRChat_jpeg");
                    PngDirTextBox.Text = Path.Combine(parent, "VRChat_png");
                }
            }
        }

        if (_step >= 3)
        {
            return;
        }

        _step++;
        RefreshStepUi();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceDirTextBox.Text.Trim();
        var jpeg = JpegDirTextBox.Text.Trim();
        var png = PngDirTextBox.Text.Trim();
        var pngMode = WizardPngModeComboBox.SelectedIndex switch
        {
            1 => PngHandlingMode.Copy,
            2 => PngHandlingMode.Delete,
            _ => PngHandlingMode.Move
        };

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(jpeg))
        {
            ValidationTextBlock.Text = "入力フォルダとJPEG出力先を指定してください。";
            return;
        }

        if (pngMode != PngHandlingMode.Delete && string.IsNullOrWhiteSpace(png))
        {
            ValidationTextBlock.Text = "PNG保管先を指定してください。";
            return;
        }

        if (!Directory.Exists(source))
        {
            ValidationTextBlock.Text = "入力フォルダが存在しません。";
            return;
        }

        Directory.CreateDirectory(jpeg);
        if (pngMode != PngHandlingMode.Delete)
        {
            Directory.CreateDirectory(png);
        }

        var id = Guid.NewGuid().ToString("N");
        CreatedConfig = new AppConfig
        {
            DefaultProfileId = id,
            MonitorEnabledOnStartup = StartMonitoringCheckBox.IsChecked == true,
            LaunchOnWindowsStartup = LaunchOnWindowsStartupCheckBox.IsChecked == true,
            Profiles = new List<ConversionProfile>
            {
                new()
                {
                    Id = id,
                    Name = "VRChat_Default",
                    SourceDir = source,
                    JpegOutputDir = jpeg,
                    PngArchiveDir = png,
                    PngHandlingMode = pngMode,
                    JpegQuality = 90,
                    IncludeSubdirectories = true,
                    DuplicatePolicy = DuplicatePolicy.Rename,
                    DryRun = false,
                    RecentFileGuardSeconds = 10
                }
            },
            WatchTargets = new List<WatchTarget>
            {
                new()
                {
                    Mode = WatchTargetMode.ExeOnly,
                    ExeName = @"C:\Program Files (x86)\Steam\steamapps\common\VRChat\VRChat.exe",
                    AppId = null,
                    ProfileId = id,
                    ProfileName = "VRChat_Default"
                }
            }
        };

        StartMonitoringAfterFinish = StartMonitoringCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void WizardPngModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePngFolderEnabledState();
    }

    private void UpdatePngFolderEnabledState()
    {
        var enabled = WizardPngModeComboBox.SelectedIndex != 2;
        PngDirTextBox.IsEnabled = enabled;
        BrowsePngDirButton.IsEnabled = enabled;
    }

    private void RefreshStepUi()
    {
        Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = _step > 1;
        NextButton.Visibility = _step < 3 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepHeaderTextBlock.Text = _step switch
        {
            1 => "ステップ1/3 ゲームを選択",
            2 => "ステップ2/3 入力フォルダを選択",
            _ => "ステップ3/3 出力先を確認"
        };

        StepDescriptionTextBlock.Text = _step switch
        {
            1 => "通常は VRChat を選択したままで問題ありません。",
            2 => "スクリーンショットが保存されるフォルダを指定します。",
            _ => "JPEG出力先とPNG保管先を作成してセットアップを完了します。"
        };
    }

    private void BrowseSourceDir_Click(object sender, RoutedEventArgs e)
    {
        var selected = BrowseFolder(SourceDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SourceDirTextBox.Text = selected;
        }
    }

    private void BrowseJpegDir_Click(object sender, RoutedEventArgs e)
    {
        var selected = BrowseFolder(JpegDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            JpegDirTextBox.Text = selected;
        }
    }

    private void BrowsePngDir_Click(object sender, RoutedEventArgs e)
    {
        var selected = BrowseFolder(PngDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PngDirTextBox.Text = selected;
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
