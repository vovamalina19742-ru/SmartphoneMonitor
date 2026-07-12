namespace SmartphoneMonitor.Models
{
    public class ChronicSeller
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;
        public int ListingCount { get; set; }
        public int UniqueBrands { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
