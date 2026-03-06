using Core.Models;

namespace Core;

public static class AppConfigValidator
{
    public static List<ValidationError> ValidateForRun(AppConfig config)
    {
        return ValidateForRun(new ConversionProfile
        {
            SourceDir = config.SourceDir,
            JpegOutputDir = config.JpegOutputDir,
            PngArchiveDir = config.PngArchiveDir,
            PngHandlingMode = config.PngHandlingMode,
            JpegQuality = config.JpegQuality,
            IncludeSubdirectories = config.IncludeSubdirectories,
            DuplicatePolicy = config.DuplicatePolicy,
            DryRun = config.DryRun,
            RecentFileGuardSeconds = config.RecentFileGuardSeconds
        });
    }

    public static List<ValidationError> ValidateForRun(ConversionProfile profile)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(profile.SourceDir) || !Directory.Exists(profile.SourceDir))
        {
            errors.Add(new ValidationError { Code = "E1001", Message = "入力フォルダが未設定、または存在しません。" });
        }

        if (string.IsNullOrWhiteSpace(profile.JpegOutputDir))
        {
            errors.Add(new ValidationError { Code = "E1002", Message = "JPEG出力フォルダを指定してください。" });
        }

        if (profile.PngHandlingMode != PngHandlingMode.Delete && string.IsNullOrWhiteSpace(profile.PngArchiveDir))
        {
            errors.Add(new ValidationError { Code = "E1003", Message = "PNG保管フォルダを指定してください。" });
        }

        if (!string.IsNullOrWhiteSpace(profile.SourceDir) &&
            (!string.IsNullOrWhiteSpace(profile.JpegOutputDir) || !string.IsNullOrWhiteSpace(profile.PngArchiveDir)))
        {
            var src = NormalizePath(profile.SourceDir);
            var jpg = NormalizePath(profile.JpegOutputDir);
            var png = NormalizePath(profile.PngArchiveDir);

            var sourceEqualsJpeg = !string.IsNullOrEmpty(src) && src.Equals(jpg, StringComparison.OrdinalIgnoreCase);
            var sourceEqualsPng = profile.PngHandlingMode != PngHandlingMode.Delete &&
                                  !string.IsNullOrEmpty(src) &&
                                  src.Equals(png, StringComparison.OrdinalIgnoreCase);

            if (sourceEqualsJpeg || sourceEqualsPng)
            {
                errors.Add(new ValidationError { Code = "E1004", Message = "入力フォルダと出力先を同一にできません。" });
            }
        }

        if (profile.JpegQuality is < 1 or > 100)
        {
            errors.Add(new ValidationError { Code = "E1005", Message = "JPEG品質は1〜100で指定してください。" });
        }

        return errors;
    }

    public static List<ValidationError> ValidateWatchTargets(
        IEnumerable<WatchTarget> targets,
        IReadOnlyCollection<ConversionProfile> profiles,
        string? defaultProfileId)
    {
        var errors = new List<ValidationError>();
        var profileIds = profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.ExeName))
            {
                errors.Add(new ValidationError { Code = "E1006", Message = "監視対象のexe名が未設定です。" });
                continue;
            }

            if (target.Mode == WatchTargetMode.Steam && (!target.AppId.HasValue || target.AppId.Value <= 0))
            {
                errors.Add(new ValidationError { Code = "E1006", Message = $"SteamモードではAppIDが必要です: {target.ExeName}" });
            }

            if (!string.IsNullOrWhiteSpace(target.ProfileId) && !profileIds.Contains(target.ProfileId))
            {
                errors.Add(new ValidationError { Code = "E1007", Message = $"監視対象に紐づくルールが存在しません: {target.ExeName}" });
            }

            if (string.IsNullOrWhiteSpace(target.ProfileId) && string.IsNullOrWhiteSpace(defaultProfileId))
            {
                errors.Add(new ValidationError { Code = "E1008", Message = $"既定ルールが未設定のため監視設定が解決できません: {target.ExeName}" });
            }
        }

        return errors;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }
}
