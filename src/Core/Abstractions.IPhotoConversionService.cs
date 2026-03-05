using Core.Models;

namespace Core.Abstractions;

public interface IPhotoConversionService
{
    Task<RunSummary> RunAsync(
        AppConfig config,
        string triggerSource,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
