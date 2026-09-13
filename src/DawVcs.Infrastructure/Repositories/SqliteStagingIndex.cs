using Dapper;

using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using Microsoft.Data.Sqlite;

namespace DawVcs.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="IStagingIndex"/> using local index.db (ADR-009, FR-STG-001, FR-SCAN-011).
/// </summary>
public sealed class SqliteStagingIndex : IStagingIndex, IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private bool _initialized;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteStagingIndex(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var dotDawvc = Path.Combine(Path.GetFullPath(rootPath), ".dawvc");
        Directory.CreateDirectory(dotDawvc);

        _dbPath = Path.Combine(dotDawvc, "index.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await connection.ExecuteAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;").ConfigureAwait(false);

                const string createSql = @"
CREATE TABLE IF NOT EXISTS staging_entries (
    path TEXT PRIMARY KEY,
    blob_id TEXT NOT NULL,
    hash TEXT NOT NULL,
    size INTEGER NOT NULL,
    role INTEGER NOT NULL,
    staged_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS filesystem_cache (
    path TEXT PRIMARY KEY,
    file_length INTEGER NOT NULL,
    last_modified_utc INTEGER NOT NULL,
    blake3_hash TEXT NOT NULL,
    checked_at INTEGER NOT NULL
);
";
                await connection.ExecuteAsync(createSql).ConfigureAwait(false);
                _initialized = true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 11 /* SQLITE_CORRUPT */ || ex.SqliteErrorCode == 26 /* SQLITE_NOTADB */)
            {
                // Crash recovery / rebuildable cache (R-006): purge corrupted db and recreate
                SqliteConnection.ClearAllPools();
                TryDeleteDatabaseFiles();

                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await connection.ExecuteAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
                const string createSql = @"
CREATE TABLE IF NOT EXISTS staging_entries (
    path TEXT PRIMARY KEY,
    blob_id TEXT NOT NULL,
    hash TEXT NOT NULL,
    size INTEGER NOT NULL,
    role INTEGER NOT NULL,
    staged_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS filesystem_cache (
    path TEXT PRIMARY KEY,
    file_length INTEGER NOT NULL,
    last_modified_utc INTEGER NOT NULL,
    blake3_hash TEXT NOT NULL,
    checked_at INTEGER NOT NULL
);
";
                await connection.ExecuteAsync(createSql).ConfigureAwait(false);
                _initialized = true;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void TryDeleteDatabaseFiles()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch { }
        try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch { }
    }

    public async Task StageEntryAsync(ArtifactEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
INSERT OR REPLACE INTO staging_entries (path, blob_id, hash, size, role, staged_at)
VALUES (@path, @blob_id, @hash, @size, @role, @staged_at);
";
        await connection.ExecuteAsync(sql, new
        {
            path = entry.Path.Value,
            blob_id = entry.Blob.ToString(),
            hash = entry.Hash.ToString(),
            size = entry.Size,
            role = (int)entry.Role,
            staged_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ConfigureAwait(false);
    }

    public async Task UnstageEntryAsync(ArtifactPath path, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "DELETE FROM staging_entries WHERE path = @path;";
        await connection.ExecuteAsync(sql, new { path = path.Value }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ArtifactEntry>> GetStagedEntriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "SELECT path, blob_id, hash, size, role FROM staging_entries ORDER BY path;";
        var rows = await connection.QueryAsync<StagedEntryRow>(sql).ConfigureAwait(false);

        var entries = new List<ArtifactEntry>();
        foreach (var row in rows)
        {
            entries.Add(new ArtifactEntry(
                new ArtifactPath(row.path),
                BlobId.Parse(row.blob_id),
                ContentHash.Parse(row.hash),
                row.size,
                (ArtifactRole)row.role));
        }

        return entries;
    }

    public async Task ClearStagedEntriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "DELETE FROM staging_entries;";
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    public async Task<ContentHash?> TryGetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "SELECT blake3_hash FROM filesystem_cache WHERE path = @path AND file_length = @size AND last_modified_utc = @lastModified;";
        var hashHex = await connection.QuerySingleOrDefaultAsync<string>(sql, new
        {
            path = path.Value,
            size = size,
            lastModified = lastModifiedUtc.ToUnixTimeMilliseconds()
        }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(hashHex))
        {
            return null;
        }

        return ContentHash.TryParse(hashHex, out var hash) ? hash : null;
    }

    public async Task SetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, ContentHash hash, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
INSERT OR REPLACE INTO filesystem_cache (path, file_length, last_modified_utc, blake3_hash, checked_at)
VALUES (@path, @size, @lastModified, @hash, @checked_at);
";
        await connection.ExecuteAsync(sql, new
        {
            path = path.Value,
            size = size,
            lastModified = lastModifiedUtc.ToUnixTimeMilliseconds(),
            hash = hash.ToString(),
            checked_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private sealed class StagedEntryRow
    {
        public string path { get; set; } = string.Empty;
        public string blob_id { get; set; } = string.Empty;
        public string hash { get; set; } = string.Empty;
        public long size { get; set; }
        public int role { get; set; }
    }
}
