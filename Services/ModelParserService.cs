using System;
using System.Text.RegularExpressions;

namespace SmartphoneMonitor.Services
{
    public class ModelParserService
    {
        private static readonly string[] AppleModels = new string[32]
        {
            "15 pro max", "15 pro", "15 plus", "15", "14 pro max", "14 pro", "14 plus", "14", "13 pro max", "13 pro",
            "13 mini", "13", "12 pro max", "12 pro", "12 mini", "12", "11 pro max", "11 pro", "11", "xs max",
            "xs", "xr", "x", "se 2022", "se 2020", "se 3", "se 2", "se", "8 plus", "8",
            "7 plus", "7"
        };

        private static readonly string[] SamsungModels = new string[56]
        {
            "s24 ultra", "s24+", "s24", "s23 ultra", "s23+", "s23", "s22 ultra", "s22+", "s22", "s21 ultra",
            "s21 fe", "s21+", "s21", "s20 fe", "s20 ultra", "s20+", "s20", "note 20 ultra", "note 20", "note 10+",
            "note 10", "note 9", "a54", "a34", "a24", "a14", "a53", "a33", "a23", "a13",
            "a52s", "a52", "a72", "a32", "a22", "a12", "a51", "a71", "a31", "a21s",
            "a50", "a70", "a30", "a40", "a10", "a05", "a04", "a03", "m12", "m21",
            "m31", "m51", "m32", "m52", "z fold", "z flip"
        };

        private static readonly string[] XiaomiModels = new string[]
        {
            "14 ultra", "14 pro", "14", "13 ultra", "13 pro", "13", "13t pro", "13t", "13 lite", "12t pro", "12t", "12x", "12 lite", "11t pro", "11t", "11 lite", "11",
            "note 13 pro+", "note 13 pro", "note 13", "note 12 pro", "note 12", "note 11 pro", "note 11", "note 10 pro", "note 10s", "note 10",
            "note 9 pro", "note 9s", "note 9", "note 8 pro", "note 8", "14c", "13c", "12c", "10c", "9c", "12",
            "10", "9", "poco x6 pro", "poco x6", "poco x5 pro", "poco x5", "poco x4 pro", "poco x3 pro", "poco x3 nfc", "poco x3",
            "poco f6 pro", "poco f6", "poco f5", "poco f4", "poco m6 pro", "poco m5s", "poco m5", "poco m4"
        };

        private static readonly string[] GoogleModels = new string[14]
        {
            "pixel 8 pro", "pixel 8a", "pixel 8", "pixel 7 pro", "pixel 7a", "pixel 7", "pixel 6 pro", "pixel 6a", "pixel 6", "pixel 5a",
            "pixel 5", "pixel 4a", "pixel 4 xl", "pixel 4"
        };

        private readonly MatchingClientService _matchingClient = new MatchingClientService();

        public string ExtractModel(string title, string brand, bool useSemantic = false)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Другой";
            }

            if (useSemantic)
            {
                // Попытка семантического сопоставления через локальный Python API
                var vectorMatchTask = _matchingClient.MatchDeviceAsync(title, brand);
                var apiResult = vectorMatchTask.GetAwaiter().GetResult();
                if (apiResult != null && apiResult.Value.Score >= 0.50)
                {
                    return apiResult.Value.MatchedModel;
                }
            }

            string text = title.ToLowerInvariant();
            
            // Очистка явных цен (например, "1300 lei", "450 €") чтобы они не слипались с названием модели
            text = Regex.Replace(text, @"\b\d{3,5}\s*(lei|mdl|лей|€|\$)\b", " ", RegexOptions.IgnoreCase);
            // Очистка 3-4 значных цифр в конце строки (скорее всего это цена без валюты)
            text = Regex.Replace(text, @"\b(1\d{3}|[2-9]\d{2,3})\b\s*$", " ", RegexOptions.IgnoreCase);
            text = text.Replace("  ", " ").Trim();
            string[]? modelList = brand.ToLowerInvariant() switch
            {
                "apple" => AppleModels,
                "samsung" => SamsungModels,
                "xiaomi" => XiaomiModels,
                "google" => GoogleModels,
                _ => null
            };

            if (modelList != null)
            {
                foreach (string modelCandidate in modelList)
                {
                    string pattern = "\\b" + Regex.Escape(modelCandidate) + "\\b";
                    if (!Regex.IsMatch(text, pattern))
                    {
                        continue;
                    }

                    string[] words = modelCandidate.Split(' ');
                    for (int i = 0; i < words.Length; i++)
                    {
                        if (words[i].Length > 0)
                        {
                            words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
                        }
                    }

                    string normalizedModel = string.Join(" ", words);
                    normalizedModel = CleanNormalizedModel(normalizedModel);
                    if (brand.Equals("apple", StringComparison.OrdinalIgnoreCase))
                    {
                        return "iPhone " + normalizedModel;
                    }
                    if (brand.Equals("samsung", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Galaxy " + normalizedModel;
                    }
                    return normalizedModel;
                }
            }

            string result = text;
            string normalizedBrand = brand.ToLowerInvariant();
            int brandIndex = result.IndexOf(normalizedBrand, StringComparison.OrdinalIgnoreCase);
            if (brandIndex >= 0)
            {
                result = result.Substring(brandIndex + normalizedBrand.Length).Trim();
            }

            result = Regex.Replace(result, "\\b(telefon|smartfon|telefoane|оригинал|original)\\b", "", RegexOptions.IgnoreCase).Trim();
            var match = Regex.Match(result, "^([a-zA-Z0-9+-]{2,})\\s*([a-zA-Z0-9+-]{2,})?");
            if (match.Success)
            {
                string primary = match.Groups[1].Value;
                string secondary = match.Groups[2].Value;
                primary = char.ToUpper(primary[0]) + primary.Substring(1).ToLowerInvariant();
                if (!string.IsNullOrEmpty(secondary) && secondary.Length >= 2)
                {
                    secondary = char.ToUpper(secondary[0]) + secondary.Substring(1).ToLowerInvariant();
                    return primary + " " + secondary;
                }
                return primary;
            }

            return "Другой";
        }

        private static string CleanNormalizedModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return model;
            // remove storage tokens like '256gb', '128 gb', '256 гб'
            model = Regex.Replace(model, "\\b(\\d{2,4})\\s*(gb|гб|tb)\\b", "", RegexOptions.IgnoreCase).Trim();
            // collapse multiple spaces
            model = Regex.Replace(model, "\\s+", " ").Trim();
            return model;
        }
    }
}
