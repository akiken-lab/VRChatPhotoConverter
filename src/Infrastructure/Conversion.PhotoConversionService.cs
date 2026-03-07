using System.Collections.Concurrent;
using Core;
using Core.Abstractions;
using Core.Models;
using System.Windows.Media.Imaging;

namespace Infrastructure.Conversion;

public sealed class PhotoConversionService : IPhotoConversionService
{
    private readonly ILogService _log;
    private readonly IProcessingHistoryStore _historyStore;
    private readonly ConcurrentDictionary<string, byte> _processedCache = new(StringComparer.OrdinalIgnoreCase);

    public PhotoConversionService(ILogService log, IProcessingHistoryStore historyStore)
    {
        _log = log;
        _historyStore = historyStore;
    }

    public async Task<RunSummary> RunAsync(
        AppConfig config,
        string triggerSource,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var summary = new RunSummary
        {
            TriggerSource = triggerSource,
            StartedAt = DateTimeOffset.Now
        };
        var skipRecentGuardCount = 0;
        var skipCacheCount = 0;
        var skipHistoryCount = 0;
        var skipDuplicateCount = 0;
        var dryRunCount = 0;

        try
        {
            var validationErrors = AppConfigValidator.ValidateForRun(config);
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException($"{validationErrors[0].Code} {validationErrors[0].Message}");
            }
            Directory.CreateDirectory(config.JpegOutputDir);

            var option = config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(config.SourceDir, "*.png", option).ToList();
            const DuplicatePolicy effectiveDuplicatePolicy = DuplicatePolicy.Overwrite;
            var pngAction = config.PngHandlingMode == PngHandlingMode.Delete ? PngHandlingMode.Delete : PngHandlingMode.Keep;
            _log.Info(
                $"run_start trigger={triggerSource} source={config.SourceDir} jpeg_out={config.JpegOutputDir} dryrun={config.DryRun} quality={config.JpegQuality} recursive={config.IncludeSubdirectories} duplicate={effectiveDuplicatePolicy} png_mode={pngAction}");
            var total = files.Count;
            var processed = 0;
            _log.Info($"run_scan_result trigger={triggerSource} png_files={total}");

            foreach (var sourcePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                summary.ScannedCount++;
                processed++;
                _log.Debug($"file_begin index={processed}/{total} path={sourcePath}");
                progress?.Report(new ConversionProgress
                {
                    TotalFiles = total,
                    ProcessedFiles = processed,
                    CurrentFile = sourcePath
                });

                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(sourcePath);
                    if ((DateTime.UtcNow - lastWrite).TotalSeconds < config.RecentFileGuardSeconds)
                    {
                        summary.SkippedCount++;
                        skipRecentGuardCount++;
                        _log.Debug($"skip_recent_guard file={sourcePath} guard_sec={config.RecentFileGuardSeconds}");
                        continue;
                    }

                    var fileSize = new FileInfo(sourcePath).Length;
                    var cacheKey = $"{sourcePath}|{lastWrite.Ticks}|{fileSize}";
                    if (_processedCache.ContainsKey(cacheKey))
                    {
                        summary.SkippedCount++;
                        skipCacheCount++;
                        _log.Debug($"skip_processed_cache file={sourcePath}");
                        continue;
                    }

                    if (await _historyStore.ExistsAsync(sourcePath, lastWrite, fileSize, cancellationToken))
                    {
                        summary.SkippedCount++;
                        skipHistoryCount++;
                        _processedCache.TryAdd(cacheKey, 0);
                        _log.Debug($"skip_processed_history file={sourcePath}");
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(config.SourceDir, sourcePath);
                    var targetJpegPath = BuildJpegPath(config.JpegOutputDir, relativePath);
                    _log.Debug($"file_paths source={sourcePath} jpeg_target={targetJpegPath}");

                    var originalJpegPath = targetJpegPath;
                    targetJpegPath = ResolveDuplicate(targetJpegPath, effectiveDuplicatePolicy, out var skipJpeg);
                    if (skipJpeg)
                    {
                        summary.SkippedCount++;
                        skipDuplicateCount++;
                        _log.Debug($"skip_duplicate file={sourcePath} duplicate_policy={effectiveDuplicatePolicy}");
                        continue;
                    }
                    if (!string.Equals(originalJpegPath, targetJpegPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Debug($"duplicate_renamed file={sourcePath} jpeg_target={targetJpegPath}");
                    }

                    if (!config.DryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetJpegPath)!);

                        ConvertPngToJpeg(sourcePath, targetJpegPath, config.JpegQuality, cancellationToken);
                        _log.Debug($"jpeg_saved source={sourcePath} target={targetJpegPath}");

                        if (pngAction == PngHandlingMode.Delete)
                        {
                            File.Delete(sourcePath);
                            _log.Debug($"png_deleted source={sourcePath}");
                        }
                        else
                        {
                            _log.Debug($"png_kept source={sourcePath}");
                        }
                    }
                    else
                    {
                        dryRunCount++;
                        _log.Debug($"dryrun_skip_write source={sourcePath} target={targetJpegPath}");
                    }

                    _processedCache.TryAdd(cacheKey, 0);
                    if (!config.DryRun)
                    {
                        await _historyStore.MarkProcessedAsync(sourcePath, lastWrite, fileSize, cancellationToken);
                    }
                    summary.ConvertedCount++;
                    summary.ArchivedCount++;
                }
                catch (Exception ex)
                {
                    summary.ErrorCount++;
                    summary.Errors.Add($"{sourcePath}: {ex.Message}");
                    _log.Error(ex, $"E2000 変換失敗: {sourcePath}");
                }
            }
        }
        catch (Exception ex)
        {
            summary.ErrorCount++;
            summary.Errors.Add($"fatal: {ex.Message}");
            _log.Error(ex, "E3000 致命エラー");
        }
        finally
        {
            summary.EndedAt = DateTimeOffset.Now;
            _log.Info(
                $"run_complete trigger={summary.TriggerSource} scanned={summary.ScannedCount} converted={summary.ConvertedCount} archived={summary.ArchivedCount} skipped={summary.SkippedCount} skip_recent={skipRecentGuardCount} skip_cache={skipCacheCount} skip_history={skipHistoryCount} skip_duplicate={skipDuplicateCount} dryrun={dryRunCount} warn={summary.WarningCount} err={summary.ErrorCount}");
        }

        return summary;
    }

    private static string BuildJpegPath(string root, string relativePngPath)
    {
        var dir = Path.GetDirectoryName(relativePngPath) ?? string.Empty;
        var file = Path.GetFileNameWithoutExtension(relativePngPath) + ".jpeg";
        return Path.Combine(root, dir, file);
    }

    private static string ResolveDuplicate(string path, DuplicatePolicy policy, out bool skip)
    {
        skip = false;
        if (!File.Exists(path))
        {
            return path;
        }

        switch (policy)
        {
            case DuplicatePolicy.Overwrite:
                return path;
            case DuplicatePolicy.Skip:
                skip = true;
                return path;
            case DuplicatePolicy.Rename:
            default:
                var dir = Path.GetDirectoryName(path)!;
                var name = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);
                var idx = 1;
                string candidate;
                do
                {
                    candidate = Path.Combine(dir, $"{name}_{idx}{ext}");
                    idx++;
                } while (File.Exists(candidate));

                return candidate;
        }
    }

    private static void ConvertPngToJpeg(string sourcePath, string targetJpegPath, int quality, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new PngBitmapDecoder(sourceStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        using var outputStream = new FileStream(targetJpegPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = Math.Clamp(quality, 1, 100)
        };
        encoder.Frames.Add(frame);
        encoder.Save(outputStream);
    }
}

