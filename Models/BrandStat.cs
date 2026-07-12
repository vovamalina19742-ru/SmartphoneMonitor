namespace SmartphoneMonitor.Models
{
    public class BrandStat
    {
        public string Brand { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal MedianPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public double Percent { get; set; }
    }
}
