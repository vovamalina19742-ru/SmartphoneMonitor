using System;
using System.Collections.Generic;

namespace SmartphoneMonitor.Models
{
    public class AnalysisResult
    {
        public DateTime AnalysisDate { get; set; } = DateTime.Now;
        public TimeSpan AnalysisDuration { get; set; }
        public int TotalListings { get; set; }
        public int PrivateListings { get; set; }
        public int CommercialListings { get; set; }
        public int FilteredByBlacklist { get; set; }
        public List<BrandStat> BrandStats { get; set; } = new List<BrandStat>();
        public List<ChronicSeller> ChronicSellers { get; set; } = new List<ChronicSeller>();
        public decimal MedianPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public List<HotDeal> HotDeals { get; set; } = new List<HotDeal>();
        public List<Listing> AllPrivateListings { get; set; } = new List<Listing>();
    }
}
