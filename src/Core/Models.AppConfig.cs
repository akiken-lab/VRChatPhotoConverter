namespace Core.Models;

public sealed class AppConfig
{
    // Legacy keys kept for backward compatibility and migration.
    public string SourceDir { get; set; } = string.Empty;
    public string JpegOutputDir { get; set; } = string.Empty;
    public string PngArchiveDir { get; set; } = string.Empty;
    public PngHandlingMode PngHandlingMode { get; set; } = PngHandlingMode.Move;
    public int JpegQuality { get; set; } = 90;
    public bool IncludeSubdirectories { get; set; } = true;
    public DuplicatePolicy DuplicatePolicy { get; set; } = DuplicatePolicy.Rename;
    public bool DryRun { get; set; }
    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Information;
    public bool LaunchOnWindowsStartup { get; set; }
    public int RecentFileGuardSeconds { get; set; } = 10;
    public bool MonitorEnabledOnStartup { get; set; }
    public string? DefaultProfileId { get; set; }
    public List<ConversionProfile> Profiles { get; set; } = new();
    public List<WatchTarget> WatchTargets { get; set; } = new();
}
