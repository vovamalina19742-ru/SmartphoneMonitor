namespace SmartphoneMonitor.Models
{
    public class RetailPromoPrice
    {
        public string Shop { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int StorageGB { get; set; }
        public decimal Price { get; set; }
        public decimal OldPrice { get; set; }
        public decimal Discount { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
