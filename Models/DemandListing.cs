using System;

namespace SmartphoneMonitor.Models
{
    public class DemandListing
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal BudgetPrice { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string DateAdded { get; set; } = string.Empty;
        public string AuthorLogin { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int StorageGB { get; set; }
        public bool IsProcessed { get; set; }
    }
}
