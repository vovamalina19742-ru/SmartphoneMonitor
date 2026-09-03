using System;
using System.Collections.Generic;

namespace SmartphoneMonitor.Models
{
    public enum QuantRating
    {
        SniperDeal,   // 80-100: Золотая сделка, высокая маржа при низком риске
        GoodBuy,      // 65-79: Выгодная цена ниже рынка
        FairMarket,   // 40-64: Честная рыночная цена
        Overpriced,   // 20-39: Завышенная цена
        ScamRisk      // 0-19 или риск-флаг: Аномальная цена / подозрительное объявление
    }

    public class QuantEvaluationResult
    {
        public string ModelName { get; set; } = string.Empty;
        public decimal ActualPrice { get; set; }
        public decimal FairValuePrice { get; set; }
        public double DiscountPercent { get; set; }
        public decimal EstimatedNetMargin { get; set; }
        public int QuantScore { get; set; } // 0 - 100
        public QuantRating Rating { get; set; }
        public string BadgeText { get; set; } = string.Empty;
        public string BadgeColor { get; set; } = string.Empty;
        public List<string> Factors { get; set; } = new();
        public bool IsHighRisk { get; set; }
        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    }
}
