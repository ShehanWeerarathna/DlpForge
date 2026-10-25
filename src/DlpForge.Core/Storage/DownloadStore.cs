using System.Text.Json;
using DlpForge.Core.Models;
using Microsoft.Data.Sqlite;

namespace DlpForge.Core.Storage;

/// <summary>SQLite-backed store for the queue and history.</summary>
public sealed class DownloadStore
{
    private readonly string _connectionString;
    private readonly Lock _lock = new();

    public DownloadStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS downloads (
                id TEXT PRIMARY KEY,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                uploader TEXT,
                thumbnail TEXT,
                duration REAL,
                options TEXT NOT NULL,
                status TEXT NOT NULL,
                file_path TEXT,
                size_bytes INTEGER,
                error TEXT,
                log_path TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT,
                hidden INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_downloads_created ON downloads(created_at);
            """);
        Migrate(c);
    }

    /// <summary>Adds columns that newer versions use to a database made by an older one.</summary>
    private static void Migrate(SqliteConnection c)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(downloads)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) columns.Add(r.GetString(1));
        }
        // 0.2: scheduled downloads.
        if (!columns.Contains("scheduled_at")) Exec(c, "ALTER TABLE downloads ADD COLUMN scheduled_at TEXT");
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Upsert(DownloadRecord r)
    {
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO downloads (id, url, title, uploader, thumbnail, duration, options, status, file_path,
                                       size_bytes, error, log_path, created_at, completed_at, hidden, scheduled_at)
                VALUES ($id, $url, $title, $uploader, $thumb, $duration, $options, $status, $file,
                        $size, $error, $log, $created, $completed, $hidden, $scheduled)
                ON CONFLICT(id) DO UPDATE SET
                    url = excluded.url, title = excluded.title, uploader = excluded.uploader,
                    thumbnail = excluded.thumbnail, duration = excluded.duration, options = excluded.options,
                    status = excluded.status, file_path = excluded.file_path, size_bytes = excluded.size_bytes,
                    error = excluded.error, log_path = excluded.log_path, completed_at = excluded.completed_at,
                    hidden = excluded.hidden, scheduled_at = excluded.scheduled_at;
                """;
            cmd.Parameters.AddWithValue("$id", r.Id);
            cmd.Parameters.AddWithValue("$url", r.Url);
            cmd.Parameters.AddWithValue("$title", r.Title);
            cmd.Parameters.AddWithValue("$uploader", (object?)r.Uploader ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$thumb", (object?)r.Thumbnail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$duration", (object?)r.DurationSeconds ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$options", JsonSerializer.Serialize(r.Options, AppSettings.Json));
            cmd.Parameters.AddWithValue("$status", r.Status.ToString());
            cmd.Parameters.AddWithValue("$file", (object?)r.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$size", (object?)r.SizeBytes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)r.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$log", (object?)r.LogPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", r.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$completed", (object?)r.CompletedAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hidden", r.HiddenFromQueue ? 1 : 0);
            cmd.Parameters.AddWithValue("$scheduled", (object?)r.ScheduledAt?.ToString("O") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM downloads WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public List<DownloadRecord> LoadAll()
    {
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM downloads ORDER BY created_at";
            using var r = cmd.ExecuteReader();
            var list = new List<DownloadRecord>();
            while (r.Read())
            {
                list.Add(new DownloadRecord
                {
                    Id = r.GetString(r.GetOrdinal("id")),
                    Url = r.GetString(r.GetOrdinal("url")),
                    Title = r.GetString(r.GetOrdinal("title")),
                    Uploader = Get<string>(r, "uploader"),
                    Thumbnail = Get<string>(r, "thumbnail"),
                    DurationSeconds = r.IsDBNull(r.GetOrdinal("duration")) ? null : r.GetDouble(r.GetOrdinal("duration")),
                    Options = JsonSerializer.Deserialize<DownloadOptions>(r.GetString(r.GetOrdinal("options")), AppSettings.Json)
                              ?? new DownloadOptions(),
                    Status = Enum.TryParse<DownloadStatus>(r.GetString(r.GetOrdinal("status")), out var s) ? s : DownloadStatus.Failed,
                    FilePath = Get<string>(r, "file_path"),
                    SizeBytes = r.IsDBNull(r.GetOrdinal("size_bytes")) ? null : r.GetInt64(r.GetOrdinal("size_bytes")),
                    Error = Get<string>(r, "error"),
                    LogPath = Get<string>(r, "log_path"),
                    CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
                    CompletedAt = Get<string>(r, "completed_at") is { } done ? DateTimeOffset.Parse(done) : null,
                    HiddenFromQueue = r.GetInt64(r.GetOrdinal("hidden")) != 0,
                    ScheduledAt = Get<string>(r, "scheduled_at") is { } at ? DateTimeOffset.Parse(at) : null,
                });
            }
            return list;
        }
    }

    private static T? Get<T>(SqliteDataReader r, string col) where T : class
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetFieldValue<T>(i);
    }
}
