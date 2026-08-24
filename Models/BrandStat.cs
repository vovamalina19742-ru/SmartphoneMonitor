namespace SmartphoneMonitor.Models
{
    public class BrandStat
    {
        public string Brand { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percent { get; set; }

        /// <summary>Median price after IQR filter.</summary>
        public decimal MedianPrice { get; set; }
        /// <summary>Average price after IQR filter.</summary>
        public decimal AveragePrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }

        /// <summary>Low / Medium / High — confidence based on sample count.</summary>
        public string Confidence { get; set; } = "Low";

        /// <summary>Budget / MidRange / Premium / Flagship — price tier classification.</summary>
        public string Tier { get; set; } = "MidRange";

        /// <summary>Standard deviation of prices (after IQR filter).</summary>
        public decimal StdDev { get; set; }
    }
}