namespace Core.Models;

public sealed class WatchTarget
{
    public WatchTargetMode Mode { get; set; } = WatchTargetMode.ExeOnly;
    public string ExeName { get; set; } = string.Empty;
    public int? AppId { get; set; }
    public string? ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;

    public override string ToString()
    {
        var target = Mode == WatchTargetMode.Steam && AppId.HasValue
            ? $"Steam({AppId}) - {ExeName}"
            : ExeName;
        return string.IsNullOrWhiteSpace(ProfileName) ? target : $"{target} -> {ProfileName}";
    }
}
