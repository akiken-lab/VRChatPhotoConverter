using Core.Models;

namespace Core.Abstractions;

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
    string GetDataDirectory();
    string GetLogDirectory();
}
