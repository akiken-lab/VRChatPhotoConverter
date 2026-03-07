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

    public JsonConfigStore()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseDir = Path.Combine(local, "VRCJpegAutoGenerator");
        _configPath = Path.Combine(_baseDir, "settings.json");
        _logsDir = Path.Combine(_baseDir, "logs");
    }

    public string GetDataDirectory() => _baseDir;
    public string GetLogDirectory() => _logsDir;

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
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
}
