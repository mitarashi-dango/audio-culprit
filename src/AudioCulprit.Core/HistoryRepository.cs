using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace AudioCulprit.Core;

public sealed class HistoryRepository : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    public HistoryRepository(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();
        Run("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY, started TEXT NOT NULL, payload TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_started ON events(started); CREATE TABLE IF NOT EXISTS ignores(identity TEXT PRIMARY KEY, payload TEXT NOT NULL, created TEXT NOT NULL);");
        Prune();
    }
    private void Run(string sql)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }
    public void Save(AudioEvent e)
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "INSERT INTO events VALUES($id,$start,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload WHERE json_extract(excluded.payload,'$.DurationMs') > json_extract(events.payload,'$.DurationMs') OR (json_extract(excluded.payload,'$.DurationMs') = json_extract(events.payload,'$.DurationMs') AND (json_extract(excluded.payload,'$.EndTimeUtc') IS NOT NULL OR json_extract(events.payload,'$.EndTimeUtc') IS NULL))";
            c.Parameters.AddWithValue("$id", e.Id);
            c.Parameters.AddWithValue("$start", e.StartTimeUtc.ToString("O"));
            c.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(e));
            c.ExecuteNonQuery();
        }
    }
    public List<AudioEvent> Load()
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT payload FROM events ORDER BY started DESC LIMIT 10000";
            using var r = c.ExecuteReader();
            var items = new List<AudioEvent>();
            while (r.Read())
            {
                var e = JsonSerializer.Deserialize<AudioEvent>(r.GetString(0))!;
                items.Add(e.EndTimeUtc == null ? e with
                {
                    EndTimeUtc = e.StartTimeUtc.AddMilliseconds(e.DurationMs)
                } : e);
            }
            return items;
        }
    }
    public List<IgnoreRule> Rules()
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT payload FROM ignores";
            using var r = c.ExecuteReader();
            var items = new List<IgnoreRule>();
            while (r.Read())
                items.Add(JsonSerializer.Deserialize<IgnoreRule>(r.GetString(0))!);
            return items;
        }
    }
    public void Ignore(IgnoreRule rule, bool remove = false)
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = remove ? "DELETE FROM ignores WHERE identity=$id" : "INSERT OR REPLACE INTO ignores VALUES($id,$payload,$created)";
            c.Parameters.AddWithValue("$id", (rule.ProcessPath ?? rule.ProcessName).ToUpperInvariant());
            if (!remove)
            {
                c.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(rule));
                c.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
            }
            c.ExecuteNonQuery();
        }
    }
    public void Clear()
    {
        lock (gate)
        {
            Run("DELETE FROM events; PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        }
    }
    public void Prune()
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "DELETE FROM events WHERE started < $cutoff OR id IN (SELECT id FROM events ORDER BY started DESC LIMIT -1 OFFSET 10000)";
            c.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-7).ToString("O"));
            c.ExecuteNonQuery();
        }
    }
    public void Dispose()
    {
        lock (gate)
            db.Dispose();
    }
}

