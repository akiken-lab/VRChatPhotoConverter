namespace Core.Models;

public sealed class RunSummary
{
    public string TriggerSource { get; set; } = "manual";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset EndedAt { get; set; } = DateTimeOffset.Now;
    public int ScannedCount { get; set; }
    public int ConvertedCount { get; set; }
    public int ArchivedCount { get; set; }
    public int SkippedCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
