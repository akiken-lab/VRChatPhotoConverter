using Core.Abstractions;
using Core.Models;
using Serilog;
using Serilog.Events;

namespace Infrastructure.Logging;

public sealed class LogService : ILogService
{
    private readonly ILogger _logger;

    public LogService(string logDirectory, AppLogLevel level = AppLogLevel.Information)
    {
        Directory.CreateDirectory(logDirectory);
        _logger = new LoggerConfiguration()
            .MinimumLevel.Is(MapLevel(level))
            .WriteTo.File(
                Path.Combine(logDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Debug(string message) => _logger.Debug(message);
    public void Info(string message) => _logger.Information(message);
    public void Warn(string message) => _logger.Warning(message);
    public void Error(string message) => _logger.Error(message);
    public void Error(Exception ex, string message) => _logger.Error(ex, message);

    private static LogEventLevel MapLevel(AppLogLevel level) =>
        level switch
        {
            AppLogLevel.Error => LogEventLevel.Error,
            AppLogLevel.Warning => LogEventLevel.Warning,
            AppLogLevel.Debug => LogEventLevel.Debug,
            _ => LogEventLevel.Information
        };
}
