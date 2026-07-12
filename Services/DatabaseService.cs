using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartphoneMonitor");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _dbPath = Path.Combine(folder, "smartphone_monitor.db");
            _connectionString = $"Data Source={_dbPath};";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Enable WAL mode for concurrency and performance
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    command.ExecuteNonQuery();
                }

                // Create tables
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Blacklist (
                            PhoneNumber TEXT PRIMARY KEY,
                            Reason TEXT,
                            DateAdded TEXT
                        );
                        CREATE TABLE IF NOT EXISTS PriceHistory (
                            Url TEXT PRIMARY KEY,
                            Price REAL,
                            LastUpdated TEXT
                        );
                        CREATE TABLE IF NOT EXISTS Settings (
                            Key TEXT PRIMARY KEY,
                            Value TEXT
                        );
                        CREATE INDEX IF NOT EXISTS idx_price_history_last_updated ON PriceHistory(LastUpdated);
                    ";
                    command.ExecuteNonQuery();
                }
            }
        }

        public List<BlacklistEntry> GetBlacklist()
        {
            var list = new List<BlacklistEntry>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT PhoneNumber, Reason, DateAdded FROM Blacklist ORDER BY DateAdded DESC;";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new BlacklistEntry
                            {
                                PhoneNumber = reader.GetString(0),
                                Reason = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                DateAdded = DateTime.TryParse(reader.GetString(2), out var dt) ? dt : DateTime.Now
                            });
                        }
                    }
                }
            }
            return list;
        }

        public void AddBlacklist(string phone, string reason)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO Blacklist (PhoneNumber, Reason, DateAdded)
                        VALUES ($phone, $reason, $date);
                    ";
                    command.Parameters.AddWithValue("$phone", phone);
                    command.Parameters.AddWithValue("$reason", reason);
                    command.Parameters.AddWithValue("$date", DateTime.Now.ToString("o"));
                    command.ExecuteNonQuery();
                }
            }
        }

        public void RemoveBlacklist(string phone)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM Blacklist WHERE PhoneNumber = $phone;";
                    command.Parameters.AddWithValue("$phone", phone);
                    command.ExecuteNonQuery();
                }
            }
        }

        public Dictionary<string, decimal> GetPriceHistory()
        {
            var history = new Dictionary<string, decimal>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Url, Price FROM PriceHistory;";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            history[reader.GetString(0)] = (decimal)reader.GetDouble(1);
                        }
                    }
                }
            }
            return history;
        }

        public void SavePriceHistory(List<Listing> listings)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT OR REPLACE INTO PriceHistory (Url, Price, LastUpdated)
                            VALUES ($url, $price, $date);
                        ";
                        
                        var urlParam = command.Parameters.Add("$url", SqliteType.Text);
                        var priceParam = command.Parameters.Add("$price", SqliteType.Real);
                        var dateParam = command.Parameters.Add("$date", SqliteType.Text);

                        string now = DateTime.Now.ToString("o");

                        foreach (var l in listings)
                        {
                            if (!string.IsNullOrEmpty(l.Url) && l.PriceValue > 0m)
                            {
                                urlParam.Value = l.Url;
                                priceParam.Value = (double)l.PriceValue;
                                dateParam.Value = now;
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public void CleanPriceHistory(int daysThreshold = 30)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    // Using strftime to subtract days correctly in sqlite
                    command.CommandText = "DELETE FROM PriceHistory WHERE datetime(LastUpdated) < datetime('now', '-' || $days || ' days');";
                    command.Parameters.AddWithValue("$days", daysThreshold);
                    command.ExecuteNonQuery();
                }
            }
        }

        public string GetSetting(string key, string defaultValue)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
                    command.Parameters.AddWithValue("$key", key);
                    var val = command.ExecuteScalar();
                    return val != null ? val.ToString()! : defaultValue;
                }
            }
        }

        public void SaveSetting(string key, string value)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO Settings (Key, Value)
                        VALUES ($key, $value);
                    ";
                    command.Parameters.AddWithValue("$key", key);
                    command.Parameters.AddWithValue("$value", value);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
