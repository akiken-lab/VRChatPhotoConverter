namespace PhotoConverterApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            UiDiagnostics.LogException("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(
                "予期しないエラーが発生しました。ログを保存しました。",
                "エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                UiDiagnostics.LogException("AppDomainUnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            UiDiagnostics.LogException("TaskSchedulerUnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}
