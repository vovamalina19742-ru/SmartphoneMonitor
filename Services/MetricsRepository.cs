using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SmartphoneMonitor.Services
{
    public class MetricsRepository
    {
        private readonly string _dbPath;

        public MetricsRepository()
        {
            _dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "metrics.db");
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS metrics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    value INTEGER NOT NULL,
    ts DATETIME NOT NULL
);
";
                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }

        public void InsertMetric(string name, long value)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO metrics (name, value, ts) VALUES ($name, $value, $ts);";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$value", value);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }

        public IEnumerable<(string name, long value, DateTime ts)> QueryRecent(string namePrefix, TimeSpan window)
        {
            var results = new List<(string, long, DateTime)>();
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name, value, ts FROM metrics WHERE name LIKE $prefix AND ts >= $since ORDER BY ts ASC";
                cmd.Parameters.AddWithValue("$prefix", namePrefix + "%");
                cmd.Parameters.AddWithValue("$since", DateTime.UtcNow - window);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var n = rdr.GetString(0);
                    var v = rdr.GetInt64(1);
                    var ts = rdr.GetDateTime(2);
                    results.Add((n, v, ts));
                }
            }
            catch
            {
            }
            return results;
        }
    }
}
