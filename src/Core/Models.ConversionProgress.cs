namespace Core.Models;

public sealed class ConversionProgress
{
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
}
