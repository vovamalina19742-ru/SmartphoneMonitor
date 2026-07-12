using System;

namespace SmartphoneMonitor.Models
{
    public class HotDeal
    {
        public double RecommendationScore { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public decimal PriceValue { get; set; }
        public decimal BrandMedian { get; set; }
        public double DiscountPercent { get; set; }
        public string Url { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;
        public int Views { get; set; }
        public int BatteryHealth { get; set; }
        public int StorageGB { get; set; }
        public int DaysOld { get; set; } = -1;
        public DateTime PostedDate { get; set; } = DateTime.Now;

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

        public string ViewsLabel
        {
            get
            {
                if (Views <= 0)
                {
                    return "";
                }
                return $"👁 {Views}";
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
    }
}
