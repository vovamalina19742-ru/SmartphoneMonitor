using System;

namespace SmartphoneMonitor.Models
{
    public class Listing
    {
        public string Title { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public decimal PriceValue { get; set; }
        public string PriceDisplay { get; set; } = string.Empty;
        public int StorageGB { get; set; }
        public int DaysOld { get; set; } = -1;
        public int Views { get; set; }
        public string SellerName { get; set; } = string.Empty;
        public string SellerType { get; set; } = "Private";
        public string PhoneNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime PostedDate { get; set; } = DateTime.Now;
        public string AuthorLogin { get; set; } = string.Empty;
        public bool IsCommercial { get; set; }
        public bool IsUrgent { get; set; }
        public string Model { get; set; } = string.Empty;
        public decimal ModelAveragePrice { get; set; }
        public double ModelPriceDeviationPercent { get; set; }
        public string ComparisonText { get; set; } = string.Empty;
        public string ComparisonColor { get; set; } = "#555555";
        public string ComparisonBg { get; set; } = "#EEEEEE";
        public int BatteryHealth { get; set; }
        public bool IsNew { get; set; }

        public string NewSmartphoneCategory { get; set; } = "PrivateUsed";

        public string CategoryBadgeText
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "🏢 Крупный ритейл",
                    "Shop999" => "🏪 Магазин 999",
                    "PrivateNew" => "👤 Частник (Новый)",
                    _ => IsNew ? "✨ Новый" : "📱 б/у"
                };
            }
        }

        public string CategoryBadgeBg
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "#F3E8FF",
                    "Shop999" => "#E0F2FE",
                    "PrivateNew" => "#FEF3C7",
                    _ => "#F3F4F6"
                };
            }
        }

        public string CategoryBadgeFg
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "#6B21A8",
                    "Shop999" => "#0369A1",
                    "PrivateNew" => "#B45309",
                    _ => "#374151"
                };
            }
        }

        public decimal RetailPrice { get; set; }
        public string RetailShopName { get; set; } = string.Empty;
        public decimal RetailSavings { get; set; }
        public double RetailSavingsPercent { get; set; }
        public bool HasRetailComparison => RetailPrice > 0 && RetailSavings > 0;

        public string RetailComparisonText
        {
            get
            {
                if (!HasRetailComparison) return string.Empty;
                return $"🛒 В магазинах ({RetailShopName}): {RetailPrice:F0} MDL  |  💡 Реальная выгода: -{RetailSavings:F0} MDL (-{RetailSavingsPercent:F0}%)";
            }
        }

        public string StorageLabel
        {
            get
            {
                string label = "";
                if (StorageGB > 0)
                {
                    if (StorageGB != 1024)
                    {
                        label = $"{StorageGB} GB";
                    }
                    else
                    {
                        label = "1 TB";
                    }
                }
                if (BatteryHealth > 0)
                {
                    if (!string.IsNullOrEmpty(label))
                    {
                        label += " | ";
                    }
                    label += $"🔋 {BatteryHealth}%";
                }
                return label;
            }
        }

        public string AgeLabel
        {
            get
            {
                int daysOld = DaysOld;
                if (daysOld == 0) return "сегодня";
                if (daysOld == 1) return "вчера";
                if (daysOld > 1)
                {
                    return PostedDate.ToString("d MMMM", new System.Globalization.CultureInfo("ru-RU"));
                }
                return "";
            }
        }

        public System.Collections.Generic.List<string> Defects { get; set; } = new System.Collections.Generic.List<string>();
        public decimal RepairCost { get; set; }
        public decimal NetProfitMargin { get; set; }
        public bool IsStolen { get; set; }

        public string DefectsLabel
        {
            get
            {
                if (Defects == null || Defects.Count == 0)
                {
                    return string.Empty;
                }
                return "🔧 " + string.Join(", ", Defects) + (RepairCost > 0m ? $" (+{RepairCost:F0} MDL ремонт)" : "");
            }
        }

        public bool HasCriticalDefect { get; set; }

        public string MarginLabel
        {
            get
            {
                if (NetProfitMargin <= 0m)
                {
                    return string.Empty;
                }
                return $"💸 Маржа: +{NetProfitMargin:F0} MDL";
            }
        }

        public decimal RecommendedResellPrice { get; set; }
        public string AiReasoning { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> ImageUrls { get; set; } = new System.Collections.Generic.List<string>();
    }
}
