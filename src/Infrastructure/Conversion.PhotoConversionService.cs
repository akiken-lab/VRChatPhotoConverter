using System.Collections.Concurrent;
using Core;
using Core.Abstractions;
using Core.Models;
using System.Windows.Media.Imaging;

namespace Infrastructure.Conversion;

public sealed class PhotoConversionService : IPhotoConversionService
{
    private const int MaxStoredErrors = 100;
    private const int MaxParallelConversions = 4;
    private readonly ILogService _log;
    private readonly IProcessingHistoryStore _historyStore;

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
        var processedCount = 0;
        var convertedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;
        var skipRecentGuardCounter = 0;
        var skipCacheCounter = 0;
        var skipHistoryCounter = 0;
        var skipDuplicateCounter = 0;
        var dryRunCounter = 0;
        var processedCache = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var errorsLock = new object();
        var reporter = new ProgressReporter(progress);

        try
        {
            var validationErrors = AppConfigValidator.ValidateForRun(config);
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException($"{validationErrors[0].Code} {validationErrors[0].Message}");
            }

            Directory.CreateDirectory(config.JpegOutputDir);

            var option = config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            const DuplicatePolicy effectiveDuplicatePolicy = DuplicatePolicy.Overwrite;
            var pngAction = config.PngHandlingMode == PngHandlingMode.Delete ? PngHandlingMode.Delete : PngHandlingMode.Keep;

            _log.Info(
                $"run_start trigger={triggerSource} source={config.SourceDir} jpeg_out={config.JpegOutputDir} dryrun={config.DryRun} quality={config.JpegQuality} recursive={config.IncludeSubdirectories} duplicate={effectiveDuplicatePolicy} png_mode={pngAction}");

            reporter.Report(
                phase: "scan",
                isIndeterminate: true,
                totalFiles: 0,
                scannedFiles: 0,
                processedFiles: 0,
                convertedCount: 0,
                skippedCount: 0,
                errorCount: 0,
                currentFile: string.Empty,
                force: true);

            var total = CountPngFiles(config.SourceDir, option, reporter, cancellationToken);

            _log.Info($"run_scan_result trigger={triggerSource} png_files={total}");

            reporter.Report(
                phase: "process",
                isIndeterminate: total == 0,
                totalFiles: total,
                scannedFiles: total,
                processedFiles: 0,
                convertedCount: 0,
                skippedCount: 0,
                errorCount: 0,
                currentFile: string.Empty,
                force: true);

            await Parallel.ForEachAsync(
                Directory.EnumerateFiles(config.SourceDir, "*.png", option),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelConversions,
                    CancellationToken = cancellationToken
                },
                async (sourcePath, ct) =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var lastWrite = File.GetLastWriteTimeUtc(sourcePath);
                    if ((DateTime.UtcNow - lastWrite).TotalSeconds < config.RecentFileGuardSeconds)
                    {
                        Interlocked.Increment(ref skippedCount);
                        Interlocked.Increment(ref skipRecentGuardCounter);
                        _log.Debug($"skip_recent_guard file={sourcePath} guard_sec={config.RecentFileGuardSeconds}");
                        return;
                    }

                    var fileSize = new FileInfo(sourcePath).Length;
                    var cacheKey = $"{sourcePath}|{lastWrite.Ticks}|{fileSize}";
                    if (processedCache.ContainsKey(cacheKey))
                    {
                        Interlocked.Increment(ref skippedCount);
                        Interlocked.Increment(ref skipCacheCounter);
                        _log.Debug($"skip_processed_cache file={sourcePath}");
                        return;
                    }

                    if (await _historyStore.ExistsAsync(sourcePath, lastWrite, fileSize, ct))
                    {
                        Interlocked.Increment(ref skippedCount);
                        Interlocked.Increment(ref skipHistoryCounter);
                        processedCache.TryAdd(cacheKey, 0);
                        _log.Debug($"skip_processed_history file={sourcePath}");
                        return;
                    }

                    var relativePath = Path.GetRelativePath(config.SourceDir, sourcePath);
                    var targetJpegPath = BuildJpegPath(config.JpegOutputDir, relativePath);
                    var originalJpegPath = targetJpegPath;
                    targetJpegPath = ResolveDuplicate(targetJpegPath, effectiveDuplicatePolicy, out var skipJpeg);
                    if (skipJpeg)
                    {
                        Interlocked.Increment(ref skippedCount);
                        Interlocked.Increment(ref skipDuplicateCounter);
                        _log.Debug($"skip_duplicate file={sourcePath} duplicate_policy={effectiveDuplicatePolicy}");
                        return;
                    }

                    _log.Debug($"file_begin path={sourcePath} jpeg_target={targetJpegPath}");
                    if (!string.Equals(originalJpegPath, targetJpegPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Debug($"duplicate_renamed file={sourcePath} jpeg_target={targetJpegPath}");
                    }

                    if (!config.DryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetJpegPath)!);

                        ConvertPngToJpeg(sourcePath, targetJpegPath, config.JpegQuality, ct);
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
                        Interlocked.Increment(ref dryRunCounter);
                        _log.Debug($"dryrun_skip_write source={sourcePath} target={targetJpegPath}");
                    }

                    processedCache.TryAdd(cacheKey, 0);
                    if (!config.DryRun)
                    {
                        await _historyStore.MarkProcessedAsync(sourcePath, lastWrite, fileSize, ct);
                    }

                    Interlocked.Increment(ref convertedCount);
                }
                catch (Exception ex)
                {
                    var newErrorCount = Interlocked.Increment(ref errorCount);
                    lock (errorsLock)
                    {
                        if (summary.Errors.Count < MaxStoredErrors)
                        {
                            summary.Errors.Add($"{sourcePath}: {ex.Message}");
                        }
                    }

                    _log.Error(ex, $"E2000 変換失敗: {sourcePath}");
                    reporter.Report(
                        phase: "process",
                        isIndeterminate: total == 0,
                        totalFiles: total,
                        scannedFiles: total,
                        processedFiles: Volatile.Read(ref processedCount),
                        convertedCount: Volatile.Read(ref convertedCount),
                        skippedCount: Volatile.Read(ref skippedCount),
                        errorCount: newErrorCount,
                        currentFile: sourcePath,
                        force: true);
                }
                finally
                {
                    var newProcessedCount = Interlocked.Increment(ref processedCount);
                    reporter.Report(
                        phase: "process",
                        isIndeterminate: total == 0,
                        totalFiles: total,
                        scannedFiles: total,
                        processedFiles: newProcessedCount,
                        convertedCount: Volatile.Read(ref convertedCount),
                        skippedCount: Volatile.Read(ref skippedCount),
                        errorCount: Volatile.Read(ref errorCount),
                        currentFile: sourcePath);
                }
            });

            summary.ScannedCount = total;
            summary.ConvertedCount = convertedCount;
            summary.ArchivedCount = convertedCount;
            summary.SkippedCount = skippedCount;
            summary.ErrorCount = errorCount;
            skipRecentGuardCount = skipRecentGuardCounter;
            skipCacheCount = skipCacheCounter;
            skipHistoryCount = skipHistoryCounter;
            skipDuplicateCount = skipDuplicateCounter;
            dryRunCount = dryRunCounter;
        }
        catch (Exception ex)
        {
            summary.ErrorCount = Volatile.Read(ref errorCount) + 1;
            lock (errorsLock)
            {
                if (summary.Errors.Count < MaxStoredErrors)
                {
                    summary.Errors.Add($"fatal: {ex.Message}");
                }
            }

            _log.Error(ex, "E3000 致命エラー");
        }
        finally
        {
            summary.EndedAt = DateTimeOffset.Now;
            reporter.Report(
                phase: "complete",
                isIndeterminate: false,
                totalFiles: summary.ScannedCount,
                scannedFiles: summary.ScannedCount,
                processedFiles: summary.ScannedCount,
                convertedCount: summary.ConvertedCount,
                skippedCount: summary.SkippedCount,
                errorCount: summary.ErrorCount,
                currentFile: string.Empty,
                force: true);

            _log.Info(
                $"run_complete trigger={summary.TriggerSource} scanned={summary.ScannedCount} converted={summary.ConvertedCount} archived={summary.ArchivedCount} skipped={summary.SkippedCount} skip_recent={skipRecentGuardCount} skip_cache={skipCacheCount} skip_history={skipHistoryCount} skip_duplicate={skipDuplicateCount} dryrun={dryRunCount} warn={summary.WarningCount} err={summary.ErrorCount}");
        }

        return summary;
    }

    private int CountPngFiles(
        string sourceDir,
        SearchOption option,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var _ in Directory.EnumerateFiles(sourceDir, "*.png", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            reporter.Report(
                phase: "scan",
                isIndeterminate: true,
                totalFiles: 0,
                scannedFiles: count,
                processedFiles: 0,
                convertedCount: 0,
                skippedCount: 0,
                errorCount: 0,
                currentFile: string.Empty);
        }

        reporter.Report(
            phase: "scan",
            isIndeterminate: false,
            totalFiles: count,
            scannedFiles: count,
            processedFiles: 0,
            convertedCount: 0,
            skippedCount: 0,
            errorCount: 0,
            currentFile: string.Empty,
            force: true);

        return count;
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

    private sealed class ProgressReporter
    {
        private const int ScanReportInterval = 200;
        private const int ProcessReportInterval = 10;
        private const int MinReportIntervalMs = 250;

        private readonly IProgress<ConversionProgress>? _progress;
        private long _lastReportTick = Environment.TickCount64;
        private int _lastProcessedFiles = -1;
        private int _lastScannedFiles = -1;
        private string _lastPhase = string.Empty;

        public ProgressReporter(IProgress<ConversionProgress>? progress)
        {
            _progress = progress;
        }

        public void Report(
            string phase,
            bool isIndeterminate,
            int totalFiles,
            int scannedFiles,
            int processedFiles,
            int convertedCount,
            int skippedCount,
            int errorCount,
            string currentFile,
            bool force = false)
        {
            if (_progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var phaseChanged = !string.Equals(_lastPhase, phase, StringComparison.Ordinal);
            var processedDelta = Math.Abs(processedFiles - _lastProcessedFiles);
            var scannedDelta = Math.Abs(scannedFiles - _lastScannedFiles);
            var enoughItems = processedDelta >= ProcessReportInterval || scannedDelta >= ScanReportInterval;
            var enoughTime = now - _lastReportTick >= MinReportIntervalMs;

            if (!force && !phaseChanged && !enoughItems && !enoughTime)
            {
                return;
            }

            _lastReportTick = now;
            _lastProcessedFiles = processedFiles;
            _lastScannedFiles = scannedFiles;
            _lastPhase = phase;
            _progress.Report(new ConversionProgress
            {
                Phase = phase,
                IsIndeterminate = isIndeterminate,
                TotalFiles = totalFiles,
                ScannedFiles = scannedFiles,
                ProcessedFiles = processedFiles,
                ConvertedCount = convertedCount,
                SkippedCount = skippedCount,
                ErrorCount = errorCount,
                CurrentFile = currentFile
            });
        }
    }
}
