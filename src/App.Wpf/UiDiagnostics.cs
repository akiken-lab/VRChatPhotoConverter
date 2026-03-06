using System.Text;
using System.IO;

namespace PhotoConverterApp;

internal static class UiDiagnostics
{
    private static readonly object Gate = new();

    public static void LogException(string scope, Exception ex)
    {
        Write(scope, ex.ToString());
    }

    public static void LogMessage(string scope, string message)
    {
        Write(scope, message);
    }

    private static void Write(string scope, string body)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GamePhotoAutoConverter",
                "logs");

            Directory.CreateDirectory(logDir);
            CleanupOldUiLogs(logDir, 30);

            var file = Path.Combine(logDir, $"ui-crash-{DateTime.Now:yyyyMMdd}.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{scope}]");
            sb.AppendLine(body);
            sb.AppendLine(new string('-', 80));

            lock (Gate)
            {
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    private static void CleanupOldUiLogs(string logDir, int keepDays)
    {
        foreach (var file in Directory.GetFiles(logDir, "ui-crash-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-keepDays))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore per-file cleanup errors.
            }
        }
    }
}
