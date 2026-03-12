namespace Core.Models;

public sealed class ConversionProgress
{
    public string Phase { get; init; } = "idle";
    public bool IsIndeterminate { get; init; }
    public int TotalFiles { get; init; }
    public int ScannedFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int ConvertedCount { get; init; }
    public int SkippedCount { get; init; }
    public int ErrorCount { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
}
