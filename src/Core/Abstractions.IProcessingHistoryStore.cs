namespace Core.Abstractions;

public interface IProcessingHistoryStore
{
    Task<bool> ExistsAsync(string sourcePath, DateTime lastWriteUtc, long fileSize, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string sourcePath, DateTime lastWriteUtc, long fileSize, CancellationToken cancellationToken = default);
    Task<int> ResetAsync(CancellationToken cancellationToken = default);
}
