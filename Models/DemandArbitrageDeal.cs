using System;

namespace SmartphoneMonitor.Models
{
    public class DemandArbitrageDeal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // Demand (Buyer) Info
        public string DemandId { get; set; } = string.Empty;
        public string DemandTitle { get; set; } = string.Empty;
        public decimal DemandBudget { get; set; }
        public string DemandUrl { get; set; } = string.Empty;
        public string DemandDate { get; set; } = string.Empty;
        public string DemandAuthor { get; set; } = string.Empty;
        public string DemandPhone { get; set; } = string.Empty;

        // Supply (Seller) Info
        public string SupplyId { get; set; } = string.Empty;
        public string SupplyTitle { get; set; } = string.Empty;
        public decimal SupplyPrice { get; set; }
        public string SupplyUrl { get; set; } = string.Empty;
        public string SupplyBrand { get; set; } = string.Empty;
        public string SupplyModel { get; set; } = string.Empty;
        public int SupplyStorageGB { get; set; }

        // Arbitrage Metrics
        public decimal PotentialProfit => DemandBudget - SupplyPrice;
        public double ProfitMarginPercent => DemandBudget > 0 ? (double)((DemandBudget - SupplyPrice) / DemandBudget) * 100.0 : 0.0;
        public double MatchScore { get; set; } = 1.0;
        public string Status { get; set; } = "Активна";
    }
}
