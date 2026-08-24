using System;
using System.Collections.Generic;
using SmartphoneMonitor.Services; // PriceBucket, PriceTrend

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
        /// <summary>Number of listings considered for price statistics (after dynamic range filter).</summary>
        public int ListingsConsidered { get; set; }

        /// <summary>Price histogram buckets.</summary>
        public List<PriceBucket> PriceBuckets { get; set; } = new();

        /// <summary>Price trends per brand (7d / 30d).</summary>
        public Dictionary<string, PriceTrend> BrandTrends { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<HotDeal> HotDeals { get; set; } = new List<HotDeal>();
        public List<Listing> AllPrivateListings { get; set; } = new List<Listing>();
    }
}
