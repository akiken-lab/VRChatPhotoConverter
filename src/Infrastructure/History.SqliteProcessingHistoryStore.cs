using Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Infrastructure.History;

public sealed class SqliteProcessingHistoryStore : IProcessingHistoryStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteProcessingHistoryStore(string baseDirectory)
    {
        Directory.CreateDirectory(baseDirectory);
        var dbPath = Path.Combine(baseDirectory, "processing-history.db");
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    public async Task<bool> ExistsAsync(string sourcePath, DateTime lastWriteUtc, long fileSize, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT 1
                                  FROM processed_files
                                  WHERE source_path = $path AND last_write_ticks = $ticks AND file_size = $size
                                  LIMIT 1;
                                  """;
            command.Parameters.AddWithValue("$path", sourcePath);
            command.Parameters.AddWithValue("$ticks", lastWriteUtc.Ticks);
            command.Parameters.AddWithValue("$size", fileSize);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkProcessedAsync(string sourcePath, DateTime lastWriteUtc, long fileSize, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT OR IGNORE INTO processed_files
                                  (source_path, last_write_ticks, file_size, processed_at_utc)
                                  VALUES
                                  ($path, $ticks, $size, $processed_at_utc);
                                  """;
            command.Parameters.AddWithValue("$path", sourcePath);
            command.Parameters.AddWithValue("$ticks", lastWriteUtc.Ticks);
            command.Parameters.AddWithValue("$size", fileSize);
            command.Parameters.AddWithValue("$processed_at_utc", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM processed_files;";
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS processed_files (
                                  source_path TEXT NOT NULL,
                                  last_write_ticks INTEGER NOT NULL,
                                  file_size INTEGER NOT NULL,
                                  processed_at_utc TEXT NOT NULL,
                                  PRIMARY KEY (source_path, last_write_ticks, file_size)
                              );
                              """;
        command.ExecuteNonQuery();
    }
}
