using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
            try
            {
                CleanupOldData(35);
            }
            catch { }
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
                        CREATE TABLE IF NOT EXISTS BlacklistLogins (
                            Login TEXT PRIMARY KEY,
                            Reason TEXT,
                            DateAdded TEXT
                        );
                        CREATE TABLE IF NOT EXISTS PriceHistory (
                            Url TEXT PRIMARY KEY,
                            Title TEXT,
                            Brand TEXT,
                            StorageGB INTEGER,
                            Price REAL,
                            LastUpdated TEXT
                        );
                        CREATE TABLE IF NOT EXISTS Settings (
                            Key TEXT PRIMARY KEY,
                            Value TEXT
                        );
                        CREATE TABLE IF NOT EXISTS DemandListings (
                            Id TEXT PRIMARY KEY,
                            Title TEXT,
                            BudgetPrice REAL,
                            Description TEXT,
                            Url TEXT,
                            DateAdded TEXT,
                            AuthorLogin TEXT,
                            PhoneNumber TEXT,
                            Brand TEXT,
                            Model TEXT,
                            StorageGB INTEGER,
                            IsProcessed INTEGER DEFAULT 0
                        );
                        CREATE INDEX IF NOT EXISTS idx_price_history_last_updated ON PriceHistory(LastUpdated);
                    ";
                    command.ExecuteNonQuery();
                }

                // Migration helper: Add missing columns if database is upgraded
                try
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA table_info(PriceHistory);";
                        var cols = new List<string>();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                cols.Add(reader.GetString(1));
                            }
                        }
                        if (!cols.Contains("Title"))
                        {
                            using (var alterCmd = connection.CreateCommand())
                            {
                                alterCmd.CommandText = "ALTER TABLE PriceHistory ADD COLUMN Title TEXT;";
                                alterCmd.ExecuteNonQuery();
                            }
                        }
                        if (!cols.Contains("Brand"))
                        {
                            using (var alterCmd = connection.CreateCommand())
                            {
                                alterCmd.CommandText = "ALTER TABLE PriceHistory ADD COLUMN Brand TEXT;";
                                alterCmd.ExecuteNonQuery();
                            }
                        }
                        if (!cols.Contains("StorageGB"))
                        {
                            using (var alterCmd = connection.CreateCommand())
                            {
                                alterCmd.CommandText = "ALTER TABLE PriceHistory ADD COLUMN StorageGB INTEGER DEFAULT 0;";
                                alterCmd.ExecuteNonQuery();
                            }
                        }
                        if (!cols.Contains("ImageUrls"))
                        {
                            using (var alterCmd = connection.CreateCommand())
                            {
                                alterCmd.CommandText = "ALTER TABLE PriceHistory ADD COLUMN ImageUrls TEXT;";
                                alterCmd.ExecuteNonQuery();
                            }
                        }

                        // Create search index after all migrations succeeded
                        using (var indexCmd = connection.CreateCommand())
                        {
                            indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_price_history_search ON PriceHistory(Brand, StorageGB);";
                            indexCmd.ExecuteNonQuery();
                        }
                    }
                }
                catch { }
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

        public List<BlacklistLoginEntry> GetBlacklistLogins()
        {
            var list = new List<BlacklistLoginEntry>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Login, Reason, DateAdded FROM BlacklistLogins ORDER BY DateAdded DESC;";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new BlacklistLoginEntry
                            {
                                Login = reader.GetString(0),
                                Reason = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                DateAdded = DateTime.TryParse(reader.GetString(2), out var dt) ? dt : DateTime.Now
                            });
                        }
                    }
                }
            }
            return list;
        }

        public void AddBlacklistLogin(string login, string reason)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO BlacklistLogins (Login, Reason, DateAdded)
                        VALUES ($login, $reason, $date);
                    ";
                    command.Parameters.AddWithValue("$login", login);
                    command.Parameters.AddWithValue("$reason", reason);
                    command.Parameters.AddWithValue("$date", DateTime.Now.ToString("o"));
                    command.ExecuteNonQuery();
                }
            }
        }

        public void RemoveBlacklistLogin(string login)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM BlacklistLogins WHERE Login = $login;";
                    command.Parameters.AddWithValue("$login", login);
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
                            INSERT OR REPLACE INTO PriceHistory (Url, Title, Brand, StorageGB, Price, LastUpdated, ImageUrls)
                            VALUES ($url, $title, $brand, $storage, $price, $date, $images);
                        ";
                        
                        var urlParam = command.Parameters.Add("$url", SqliteType.Text);
                        var titleParam = command.Parameters.Add("$title", SqliteType.Text);
                        var brandParam = command.Parameters.Add("$brand", SqliteType.Text);
                        var storageParam = command.Parameters.Add("$storage", SqliteType.Integer);
                        var priceParam = command.Parameters.Add("$price", SqliteType.Real);
                        var dateParam = command.Parameters.Add("$date", SqliteType.Text);
                        var imagesParam = command.Parameters.Add("$images", SqliteType.Text);

                        string now = DateTime.Now.ToString("o");

                        foreach (var l in listings)
                        {
                            if (!string.IsNullOrEmpty(l.Url) && l.PriceValue > 0m)
                            {
                                urlParam.Value = l.Url;
                                titleParam.Value = l.Title ?? string.Empty;
                                brandParam.Value = l.Brand ?? "Другие";
                                storageParam.Value = l.StorageGB;
                                priceParam.Value = (double)l.PriceValue;
                                dateParam.Value = now;
                                imagesParam.Value = (l.ImageUrls != null && l.ImageUrls.Count > 0)
                                    ? Newtonsoft.Json.JsonConvert.SerializeObject(l.ImageUrls)
                                    : string.Empty;
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public List<string> GetCachedImageUrls(string url)
        {
            if (string.IsNullOrEmpty(url)) return new List<string>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT ImageUrls FROM PriceHistory WHERE Url = $url;";
                    command.Parameters.AddWithValue("$url", url);
                    var val = command.ExecuteScalar()?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        try
                        {
                            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(val) ?? new List<string>();
                        }
                        catch { }
                    }
                }
            }
            return new List<string>();
        }

        public List<(DateTime date, decimal price, string title)> GetPriceHistoryForBrandAndModel(string brand, string titleKeyword, int storageGB)
        {
            var history = new List<(DateTime date, decimal price, string title)>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    // Clean up titleKeyword from brackets and spaces
                    string cleanKeyword = Regex.Replace(titleKeyword, @"\[.*?\]", "").Trim();
                    // Split into single words and take first two (e.g. "iPhone 13 Pro" -> "iPhone 13")
                    var words = cleanKeyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string searchPattern = words.Length > 1 ? $"%{words[0]}%{words[1]}%" : $"%{cleanKeyword}%";

                    command.CommandText = @"
                        SELECT LastUpdated, Price, Title 
                        FROM PriceHistory 
                        WHERE Brand = $brand 
                          AND StorageGB = $storage 
                          AND Title LIKE $titleKeyword
                        ORDER BY LastUpdated DESC;
                    ";
                    command.Parameters.AddWithValue("$brand", brand);
                    command.Parameters.AddWithValue("$storage", storageGB);
                    command.Parameters.AddWithValue("$titleKeyword", searchPattern);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (DateTime.TryParse(reader.GetString(0), out var date))
                            {
                                history.Add((date, (decimal)reader.GetDouble(1), reader.GetString(2)));
                            }
                        }
                    }
                }
            }
            return history;
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

        private static byte[] _optionalEntropy = System.Text.Encoding.UTF8.GetBytes("SmartphoneMonitor_v1");

        /// <summary>
        /// Encrypts a string using Windows DPAPI (ProtectedData).
        /// Falls back to base64 encoding if DPAPI is unavailable (non-Windows).
        /// </summary>
        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                    plainBytes, _optionalEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback for non-Windows: simple base64 encode (not secure, but preserves functionality)
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
            }
        }

        /// <summary>
        /// Decrypts a string using Windows DPAPI (ProtectedData).
        /// Falls back to base64 decode if DPAPI is unavailable.
        /// </summary>
        private static string DecryptString(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
                byte[] plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    encryptedBytes, _optionalEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plainBytes);
            }
            catch (FormatException)
            {
                // Not a valid base64 string, likely a plain-text token
                return encryptedBase64;
            }
            catch (PlatformNotSupportedException)
            {
                try
                {
                    return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encryptedBase64));
                }
                catch { return encryptedBase64; }
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Cryptographic failure (e.g. different user profile), return raw for safety
                return encryptedBase64;
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
                    string raw = val != null ? val.ToString()! : defaultValue;

                    // Decrypt sensitive settings
                    if (key == "TelegramToken")
                    {
                        string decrypted = DecryptString(raw);
                        // If decryption succeeded (non-empty), return it; otherwise return raw for backward compatibility
                        if (!string.IsNullOrEmpty(decrypted)) return decrypted;
                        // If raw wasn't empty but decryption returned empty, the data was stored unencrypted — return raw
                        if (!string.IsNullOrEmpty(raw)) return raw;
                    }

                    return raw;
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
                    string finalValue = value;

                    // Encrypt sensitive settings
                    if (key == "TelegramToken" && !string.IsNullOrEmpty(value))
                    {
                        finalValue = EncryptString(value);
                    }

                    command.CommandText = @"
                        INSERT OR REPLACE INTO Settings (Key, Value)
                        VALUES ($key, $value);
                    ";
                    command.Parameters.AddWithValue("$key", key);
                    command.Parameters.AddWithValue("$value", finalValue);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void ExportListingsToCsv(List<Listing> listings)
        {
            try
            {
                string reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports");
                if (!Directory.Exists(reportsDir))
                {
                    Directory.CreateDirectory(reportsDir);
                }

                string csvPath = Path.Combine(reportsDir, "listings_history.csv");
                bool writeHeader = !File.Exists(csvPath);

                using (var writer = new StreamWriter(csvPath, true, Encoding.UTF8))
                {
                    if (writeHeader)
                    {
                        writer.WriteLine("DateAdded,Brand,Model,StorageGB,IsNew,PriceValue,SellerName,PhoneNumber,AuthorLogin,Url,Title");
                    }

                    foreach (var l in listings)
                    {
                        string brandLower = l.Brand.ToLowerInvariant();
                        if (brandLower != "apple" && brandLower != "xiaomi" && brandLower != "samsung")
                        {
                            continue;
                        }

                        string dateStr = l.PostedDate.ToString("yyyy-MM-dd HH:mm:ss");
                        string brand = EscapeCsv(l.Brand);
                        string model = EscapeCsv(l.Model);
                        string storage = l.StorageGB.ToString();
                        string isNew = l.IsNew.ToString().ToLowerInvariant();
                        string price = l.PriceValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string seller = EscapeCsv(l.SellerName);
                        string phone = EscapeCsv(l.PhoneNumber);
                        string login = EscapeCsv(l.AuthorLogin);
                        string url = EscapeCsv(l.Url);
                        string title = EscapeCsv(l.Title);

                        writer.WriteLine($"{dateStr},{brand},{model},{storage},{isNew},{price},{seller},{phone},{login},{url},{title}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSV Export Error]: {ex.Message}");
            }
        }

        private string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        public void SaveDemandListings(IEnumerable<DemandListing> demandListings)
        {
            if (demandListings == null) return;
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        foreach (var item in demandListings)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = @"
                                    INSERT OR REPLACE INTO DemandListings 
                                    (Id, Title, BudgetPrice, Description, Url, DateAdded, AuthorLogin, PhoneNumber, Brand, Model, StorageGB, IsProcessed)
                                    VALUES ($id, $title, $budget, $desc, $url, $date, $login, $phone, $brand, $model, $storage, $processed);
                                ";
                                command.Parameters.AddWithValue("$id", item.Id);
                                command.Parameters.AddWithValue("$title", item.Title);
                                command.Parameters.AddWithValue("$budget", (double)item.BudgetPrice);
                                command.Parameters.AddWithValue("$desc", item.Description);
                                command.Parameters.AddWithValue("$url", item.Url);
                                command.Parameters.AddWithValue("$date", item.DateAdded);
                                command.Parameters.AddWithValue("$login", item.AuthorLogin);
                                command.Parameters.AddWithValue("$phone", item.PhoneNumber);
                                command.Parameters.AddWithValue("$brand", item.Brand);
                                command.Parameters.AddWithValue("$model", item.Model);
                                command.Parameters.AddWithValue("$storage", item.StorageGB);
                                command.Parameters.AddWithValue("$processed", item.IsProcessed ? 1 : 0);
                                command.ExecuteNonQuery();
                            }
                        }
                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveDemandListings Error]: {ex.Message}");
            }
        }

        public List<DemandListing> GetActiveDemandListings()
        {
            var list = new List<DemandListing>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Id, Title, BudgetPrice, Description, Url, DateAdded, AuthorLogin, PhoneNumber, Brand, Model, StorageGB, IsProcessed FROM DemandListings ORDER BY DateAdded DESC;";
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new DemandListing
                                {
                                    Id = reader.GetString(0),
                                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                    BudgetPrice = reader.IsDBNull(2) ? 0m : (decimal)reader.GetDouble(2),
                                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                    Url = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                    DateAdded = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                    AuthorLogin = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                    PhoneNumber = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                    Brand = reader.IsDBNull(8) ? "" : reader.GetString(8),
                                    Model = reader.IsDBNull(9) ? "" : reader.GetString(9),
                                    StorageGB = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                                    IsProcessed = !reader.IsDBNull(11) && reader.GetInt32(11) == 1
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetActiveDemandListings Error]: {ex.Message}");
            }
            return list;
        }

        public (int removedPriceHistory, int removedDemandListings) CleanupOldData(int maxAgeDays = 35)
        {
            int removedPriceHistory = 0;
            int removedDemandListings = 0;
            DateTime cutoffDate = DateTime.Now.AddDays(-maxAgeDays);
            string cutoffStr = cutoffDate.ToString("yyyy-MM-dd HH:mm:ss");

            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = "DELETE FROM PriceHistory WHERE LastUpdated < @cutoff OR Price <= 0;";
                            cmd.Parameters.AddWithValue("@cutoff", cutoffStr);
                            removedPriceHistory = cmd.ExecuteNonQuery();
                        }

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = "DELETE FROM DemandListings WHERE DateAdded < @cutoff OR IsProcessed = 1;";
                            cmd.Parameters.AddWithValue("@cutoff", cutoffStr);
                            removedDemandListings = cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA incremental_vacuum;";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DatabaseCleanup Error]: {ex.Message}");
            }

            return (removedPriceHistory, removedDemandListings);
        }
    }
}
