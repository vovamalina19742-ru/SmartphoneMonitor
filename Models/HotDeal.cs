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
    }
}
