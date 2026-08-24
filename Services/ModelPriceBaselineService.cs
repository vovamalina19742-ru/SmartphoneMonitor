using System;
using System.Collections.Generic;

namespace SmartphoneMonitor.Services
{
    public class ModelPriceBaselineInfo
    {
        public decimal BaselinePrice { get; set; }
        public bool IsLegacyBudget { get; set; }
        public string ModelGroup { get; set; } = string.Empty;
    }

    public static class ModelPriceBaselineService
    {
        private static readonly Dictionary<string, decimal> _baselineCatalog = new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Xiaomi / Redmi Legacy & Budget ---
            { "xiaomi_redmi 9a_32", 900m },
            { "xiaomi_redmi 9a_64", 1100m },
            { "xiaomi_redmi 9c_32", 950m },
            { "xiaomi_redmi 9c_64", 1200m },
            { "xiaomi_redmi 9c_128", 1400m },
            { "xiaomi_redmi 9_32", 1000m },
            { "xiaomi_redmi 9_64", 1200m },
            { "xiaomi_redmi 9_128", 1450m },
            { "xiaomi_redmi note 9_64", 1300m },
            { "xiaomi_redmi note 9_128", 1600m },
            { "xiaomi_redmi note 9s_64", 1400m },
            { "xiaomi_redmi note 9s_128", 1700m },
            { "xiaomi_redmi note 9 pro_64", 1500m },
            { "xiaomi_redmi note 9 pro_128", 1850m },

            { "xiaomi_redmi 8a_32", 800m },
            { "xiaomi_redmi 8_32", 850m },
            { "xiaomi_redmi 8_64", 1050m },
            { "xiaomi_redmi note 8_32", 950m },
            { "xiaomi_redmi note 8_64", 1200m },
            { "xiaomi_redmi note 8_128", 1450m },
            { "xiaomi_redmi note 8 pro_64", 1400m },
            { "xiaomi_redmi note 8 pro_128", 1700m },

            { "xiaomi_redmi 7a_16", 600m },
            { "xiaomi_redmi 7a_32", 700m },
            { "xiaomi_redmi 7_32", 800m },
            { "xiaomi_redmi 7_64", 1000m },
            { "xiaomi_redmi note 7_32", 900m },
            { "xiaomi_redmi note 7_64", 1150m },
            { "xiaomi_redmi note 7_128", 1350m },

            { "xiaomi_redmi 10a_32", 950m },
            { "xiaomi_redmi 10a_64", 1150m },
            { "xiaomi_redmi 10c_64", 1250m },
            { "xiaomi_redmi 10c_128", 1500m },
            { "xiaomi_redmi 10_64", 1350m },
            { "xiaomi_redmi 10_128", 1650m },
            { "xiaomi_redmi note 10_64", 1600m },
            { "xiaomi_redmi note 10_128", 1950m },
            { "xiaomi_redmi note 10 pro_64", 1900m },
            { "xiaomi_redmi note 10 pro_128", 2250m },

            { "xiaomi_redmi 12c_64", 1200m },
            { "xiaomi_redmi 12c_128", 1450m },
            { "xiaomi_redmi 13c_128", 1600m },
            { "xiaomi_redmi 13c_256", 1900m },
            { "xiaomi_redmi a1_32", 850m },
            { "xiaomi_redmi a2_32", 950m },
            { "xiaomi_redmi a2+_64", 1150m },

            // --- Samsung Legacy & Budget ---
            { "samsung_galaxy a10_32", 850m },
            { "samsung_galaxy a10s_32", 900m },
            { "samsung_galaxy a01_16", 650m },
            { "samsung_galaxy a01_32", 750m },
            { "samsung_galaxy a02_32", 800m },
            { "samsung_galaxy a02s_32", 900m },
            { "samsung_galaxy a03_32", 950m },
            { "samsung_galaxy a03_64", 1150m },
            { "samsung_galaxy a03s_32", 1000m },
            { "samsung_galaxy a03s_64", 1200m },
            { "samsung_galaxy a04_32", 1050m },
            { "samsung_galaxy a04_64", 1250m },
            { "samsung_galaxy a04s_32", 1150m },
            { "samsung_galaxy a04s_64", 1350m },
            { "samsung_galaxy a05_64", 1300m },
            { "samsung_galaxy a05_128", 1600m },

            { "samsung_galaxy a11_32", 950m },
            { "samsung_galaxy a12_32", 1100m },
            { "samsung_galaxy a12_64", 1300m },
            { "samsung_galaxy a12_128", 1550m },
            { "samsung_galaxy a13_32", 1250m },
            { "samsung_galaxy a13_64", 1450m },
            { "samsung_galaxy a13_128", 1750m },
            { "samsung_galaxy a14_64", 1500m },
            { "samsung_galaxy a14_128", 1850m },

            { "samsung_galaxy a20_32", 1000m },
            { "samsung_galaxy a20s_32", 1050m },
            { "samsung_galaxy a21s_32", 1150m },
            { "samsung_galaxy a21s_64", 1350m },
            { "samsung_galaxy a22_64", 1400m },
            { "samsung_galaxy a22_128", 1700m },
            { "samsung_galaxy a30_32", 1200m },
            { "samsung_galaxy a30_64", 1400m },
            { "samsung_galaxy a31_64", 1500m },
            { "samsung_galaxy a31_128", 1800m },
            { "samsung_galaxy a50_64", 1450m },
            { "samsung_galaxy a50_128", 1750m },
            { "samsung_galaxy a51_64", 1700m },
            { "samsung_galaxy a51_128", 2050m },

            // --- Poco Legacy & Budget ---
            { "poco_c40_32", 900m },
            { "poco_c40_64", 1150m },
            { "poco_c65_128", 1500m },
            { "poco_c65_256", 1800m },
            { "poco_m3_64", 1250m },
            { "poco_m3_128", 1500m },
            { "poco_m3 pro_64", 1400m },
            { "poco_m3 pro_128", 1700m },
            { "poco_m4 pro_128", 1750m },
            { "poco_m4 pro_256", 2100m },
            { "poco_x3 nfc_64", 1600m },
            { "poco_x3 nfc_128", 1900m },
            { "poco_x3 pro_128", 1950m },
            { "poco_x3 pro_256", 2300m }
        };

        public static ModelPriceBaselineInfo? GetBaseline(string brand, string model, int storageGB)
        {
            if (string.IsNullOrEmpty(brand) || string.IsNullOrEmpty(model))
                return null;

            string keyExact = $"{brand.ToLowerInvariant().Trim()}_{model.ToLowerInvariant().Trim()}_{storageGB}";
            if (_baselineCatalog.TryGetValue(keyExact, out decimal exactPrice))
            {
                return new ModelPriceBaselineInfo
                {
                    BaselinePrice = exactPrice,
                    IsLegacyBudget = true,
                    ModelGroup = $"{brand} {model} ({storageGB} GB)"
                };
            }

            string keyGeneral64 = $"{brand.ToLowerInvariant().Trim()}_{model.ToLowerInvariant().Trim()}_64";
            if (_baselineCatalog.TryGetValue(keyGeneral64, out decimal gen64Price))
            {
                decimal estPrice = storageGB switch
                {
                    <= 32 => Math.Round(gen64Price * 0.85m),
                    >= 128 => Math.Round(gen64Price * 1.20m),
                    _ => gen64Price
                };
                return new ModelPriceBaselineInfo
                {
                    BaselinePrice = estPrice,
                    IsLegacyBudget = true,
                    ModelGroup = $"{brand} {model}"
                };
            }

            string normModel = model.ToLowerInvariant();
            if (normModel.Contains("redmi 9") || normModel.Contains("redmi 8") || normModel.Contains("redmi 7") || normModel.Contains("redmi 6") ||
                normModel.Contains("galaxy a10") || normModel.Contains("galaxy a11") || normModel.Contains("galaxy a12") || normModel.Contains("galaxy a0") ||
                normModel.Contains("poco c"))
            {
                decimal heuristicPrice = storageGB switch
                {
                    <= 32 => 900m,
                    64 => 1200m,
                    128 => 1500m,
                    _ => 1100m
                };
                return new ModelPriceBaselineInfo
                {
                    BaselinePrice = heuristicPrice,
                    IsLegacyBudget = true,
                    ModelGroup = $"{brand} {model} (Бюджетная серия 3-5 лет)"
                };
            }

            return null;
        }
    }
}
