using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SmartphoneMonitor.Services
{
    public class OsSupportStatus
    {
        public bool IsXiaomiDevice { get; set; }
        public bool SupportsAndroid16 { get; set; }
        public bool SupportsAndroid17 { get; set; }
        public string OsStatusSummary { get; set; } = string.Empty;
    }

    public class XiaomiOsSupportPolicyConfig
    {
        public List<string> Android16SupportedPatterns { get; set; } = new List<string>();
        public List<string> Android17SupportedPatterns { get; set; } = new List<string>();
    }

    public class XiaomiOsSupportService
    {
        private List<string> _android16List = new() 
        { 
            "Xiaomi 13", "Xiaomi 14", "Xiaomi 15", "Xiaomi 16", "Xiaomi 17",
            "Redmi K60", "Redmi K70", "Redmi K80", "Redmi K90",
            "Redmi Note 13", "Redmi Note 14", "Redmi Note 15",
            "POCO F5", "POCO F6", "POCO F7", "POCO F8",
            "POCO X6", "POCO X7", "POCO X8"
        };

        private List<string> _android17List = new() 
        { 
            "Xiaomi 14", "Xiaomi 15", "Xiaomi 16", "Xiaomi 17",
            "Redmi K70", "Redmi K80", "Redmi K90",
            "Redmi Note 14", "Redmi Note 15",
            "POCO F6", "POCO F7", "POCO F8",
            "POCO X7", "POCO X8"
        };

        public XiaomiOsSupportService()
        {
            LoadPolicyFromFile();
        }

        private void LoadPolicyFromFile()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xiaomi_os_policy.json");
                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "xiaomi_os_policy.json");
                }

                if (File.Exists(jsonPath))
                {
                    string content = File.ReadAllText(jsonPath);
                    var config = JsonConvert.DeserializeObject<XiaomiOsSupportPolicyConfig>(content);
                    if (config != null)
                    {
                        if (config.Android16SupportedPatterns != null && config.Android16SupportedPatterns.Count > 0)
                        {
                            _android16List = config.Android16SupportedPatterns;
                        }
                        if (config.Android17SupportedPatterns != null && config.Android17SupportedPatterns.Count > 0)
                        {
                            _android17List = config.Android17SupportedPatterns;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[XiaomiOsSupportService] Ошибка загрузки xiaomi_os_policy.json: {ex.Message}");
            }
        }

        public OsSupportStatus AnalyzeModel(string modelTitle)
        {
            if (string.IsNullOrWhiteSpace(modelTitle))
            {
                return new OsSupportStatus { IsXiaomiDevice = false, SupportsAndroid16 = false, SupportsAndroid17 = false, OsStatusSummary = "⚠️ Модель не определена" };
            }

            string lowerTitle = modelTitle.ToLowerInvariant();
            bool isXiaomiBrand = lowerTitle.Contains("xiaomi") || lowerTitle.Contains("redmi") || lowerTitle.Contains("poco") || lowerTitle.Contains("mi ");

            if (!isXiaomiBrand)
            {
                return new OsSupportStatus
                {
                    IsXiaomiDevice = false,
                    SupportsAndroid16 = true,
                    SupportsAndroid17 = true,
                    OsStatusSummary = "Стандартный цикл обновлений"
                };
            }

            bool has16 = _android16List.Any(pattern => modelTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            bool has17 = _android17List.Any(pattern => modelTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            string summary;
            if (has17)
            {
                summary = "🟢 Android 16 & 🟢 Android 17 (Актуальный флагман/субфлагман)";
            }
            else if (has16)
            {
                summary = "🟢 Android 16 / ❌ Без Android 17 (Ограниченная поддержка)";
            }
            else
            {
                summary = "❌ Устаревшая ОС (Не поддерживает Android 16)";
            }

            return new OsSupportStatus
            {
                IsXiaomiDevice = true,
                SupportsAndroid16 = has16,
                SupportsAndroid17 = has17,
                OsStatusSummary = summary
            };
        }
    }
}