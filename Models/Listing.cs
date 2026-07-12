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
        public bool IsCommercial { get; set; }
        public bool IsUrgent { get; set; }
        public string Model { get; set; } = string.Empty;
        public decimal ModelAveragePrice { get; set; }
        public double ModelPriceDeviationPercent { get; set; }
        public string ComparisonText { get; set; } = string.Empty;
        public string ComparisonColor { get; set; } = "#555555";
        public string ComparisonBg { get; set; } = "#EEEEEE";
        public int BatteryHealth { get; set; }

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
    }
}
