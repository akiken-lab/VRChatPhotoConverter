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
    public bool ConvertExistingFilesOnFirstSetup { get; private set; }

    public FirstRunWizardWindow()
    {
        InitializeComponent();

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        SourceDirTextBox.Text = Path.Combine(pictures, "VRChat");
        JpegDirTextBox.Text = Path.Combine(pictures, "VRChat_jpeg");

        RefreshStepUi();
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

        if (_step >= 2)
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
        var pngMode = WizardPngModeComboBox.SelectedIndex switch
        {
            1 => PngHandlingMode.Delete,
            _ => PngHandlingMode.Keep
        };

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(jpeg))
        {
            ValidationTextBlock.Text = "入力フォルダとJPEG出力先を指定してください。";
            return;
        }

        if (!Directory.Exists(source))
        {
            ValidationTextBlock.Text = "入力フォルダが存在しません。";
            return;
        }

        Directory.CreateDirectory(jpeg);

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
                    PngArchiveDir = string.Empty,
                    PngHandlingMode = pngMode,
                    JpegQuality = 90,
                    IncludeSubdirectories = true,
                    DuplicatePolicy = DuplicatePolicy.Overwrite,
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
        ConvertExistingFilesOnFirstSetup = ConvertExistingFilesCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshStepUi()
    {
        Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = _step > 1;
        NextButton.Visibility = _step < 2 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepHeaderTextBlock.Text = _step switch
        {
            1 => "ステップ1/2 ゲームを選択",
            _ => "ステップ2/2 入出力フォルダと実行設定"
        };

        StepDescriptionTextBlock.Text = _step switch
        {
            1 => "通常は VRChat を選択したままで問題ありません。",
            _ => "入力フォルダ・JPEG出力先・PNG処理をまとめて設定して完了します。"
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
