using System.Text.Json;
using Core.Abstractions;
using Core.Models;

namespace Infrastructure.Config;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseDir;
    private readonly string _configPath;
    private readonly string _logsDir;
    private readonly string _legacyBaseDir;
    private readonly string _legacyConfigPath;
    private readonly string _legacyLogsDir;

    public JsonConfigStore()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseDir = Path.Combine(local, "GamePhotoAutoConverter");
        _configPath = Path.Combine(_baseDir, "settings.json");
        _logsDir = Path.Combine(_baseDir, "logs");
        _legacyBaseDir = Path.Combine(local, "SteamPhotoAutoConverter");
        _legacyConfigPath = Path.Combine(_legacyBaseDir, "settings.json");
        _legacyLogsDir = Path.Combine(_legacyBaseDir, "logs");
    }

    public string GetDataDirectory() => _baseDir;
    public string GetLogDirectory() => _logsDir;

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        MigrateLegacyIfNeeded();
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_logsDir);

        if (!File.Exists(_configPath))
        {
            return new AppConfig();
        }

        await using var stream = File.OpenRead(_configPath);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, Options, cancellationToken);
        return config ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_baseDir);
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, Options, cancellationToken);
    }

    private void MigrateLegacyIfNeeded()
    {
        if (File.Exists(_configPath))
        {
            return;
        }

        if (File.Exists(_legacyConfigPath))
        {
            Directory.CreateDirectory(_baseDir);
            File.Copy(_legacyConfigPath, _configPath, overwrite: false);
        }

        if (!Directory.Exists(_legacyLogsDir) || Directory.Exists(_logsDir))
        {
            return;
        }

        Directory.CreateDirectory(_baseDir);
        CopyDirectory(_legacyLogsDir, _logsDir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, targetSubDir);
        }
    }
}
