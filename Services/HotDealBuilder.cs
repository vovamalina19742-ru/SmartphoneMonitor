using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public static class HotDealBuilder
    {
        public static HotDeal Create(Listing listing, double recommendationScore, decimal referencePrice, double discountPercent)
        {
            string titlePrefix = (listing.Defects != null && listing.Defects.Count > 0)
                ? $"⚠️ [РИСК: {recommendationScore:F0}/100] "
                : $"⭐ [Рекомендация: {recommendationScore:F0}/100] ";

            return new HotDeal
            {
                RecommendationScore = recommendationScore,
                ArbitrageScore = recommendationScore,
                Title = titlePrefix + listing.Title,
                Brand = listing.Brand,
                PriceValue = listing.PriceValue,
                BrandMedian = referencePrice,
                DiscountPercent = discountPercent,
                Url = listing.Url,
                PhoneNumber = listing.PhoneNumber,
                SellerName = listing.SellerName,
                Views = listing.Views,
                StorageGB = listing.StorageGB,
                DaysOld = listing.DaysOld,
                PostedDate = listing.PostedDate,
                BatteryHealth = listing.BatteryHealth,
                Defects = listing.Defects,
                RepairCost = listing.RepairCost,
                NetProfitMargin = listing.NetProfitMargin,
                IsStolen = listing.IsStolen,
                AuthorLogin = listing.AuthorLogin,
                IsNew = listing.IsNew,
                NewSmartphoneCategory = listing.NewSmartphoneCategory,
                RetailPrice = listing.RetailPrice,
                RetailShopName = listing.RetailShopName,
                RetailSavings = listing.RetailSavings,
                RetailSavingsPercent = listing.RetailSavingsPercent,
                RecommendedResellPrice = listing.RecommendedResellPrice,
                AiReasoning = listing.AiReasoning,
                ImageUrls = listing.ImageUrls
            };
        }

        public static bool IsHotDeal(Listing listing, double recommendationScore)
        {
            if (listing.IsStolen || listing.HasCriticalDefect) return false;

            // Standard hot deal: high score, no defects
            if (recommendationScore >= 70.0 && (!listing.IsCommercial || listing.IsNew) && (listing.Defects == null || listing.Defects.Count == 0))
            {
                return true;
            }

            // Risky deal: decent baseline score but has defects (will be capped at 70 or 55 by evaluator, so we accept >= 40)
            if (recommendationScore >= 40.0 && (listing.Defects != null && listing.Defects.Count > 0))
            {
                return true;
            }

            return false;
        }
    }
}
