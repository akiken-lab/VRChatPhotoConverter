namespace Core.Abstractions;

public interface ILogService
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Error(Exception ex, string message);
}
