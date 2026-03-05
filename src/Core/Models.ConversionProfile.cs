namespace Core.Models;

public sealed class ConversionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新規プロファイル";
    public string SourceDir { get; set; } = string.Empty;
    public string JpegOutputDir { get; set; } = string.Empty;
    public string PngArchiveDir { get; set; } = string.Empty;
    public PngHandlingMode PngHandlingMode { get; set; } = PngHandlingMode.Move;
    public int JpegQuality { get; set; } = 90;
    public bool IncludeSubdirectories { get; set; } = true;
    public DuplicatePolicy DuplicatePolicy { get; set; } = DuplicatePolicy.Rename;
    public bool DryRun { get; set; }
    public int RecentFileGuardSeconds { get; set; } = 10;
}
