using System.Collections.Generic;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;
using Xunit;

namespace SmartphoneMonitor.Tests
{
    public class PromoScoringUnitTests
    {
        [Fact]
        public void EvaluateScore_ReflectsPromoComparison_ForNewListing()
        {
            var promos = new List<RetailPromoPrice>
            {
                new RetailPromoPrice { Shop = "ShopA", Brand = "Apple", Name = "iPhone 14 Pro Max", StorageGB = 256, Price = 25000m },
            };

            var promoService = new RetailPromoService();
            var map = promoService.BuildPromoMap(promos);

            var listing = new Listing { Brand = "Apple", Model = "iPhone 14 Pro Max", StorageGB = 256, PriceValue = 26000m, IsNew = true };

            var eval = new ListingEvaluationService();
            var promo = eval.GetBestPromo(listing, map);
            Assert.NotNull(promo);

            double score = eval.EvaluateScore(listing, 0m, promo.Price, map, null);
            eval.ApplyComparisonText(listing, 0m, "новых моделей", map);

            Assert.Contains("дороже", listing.ComparisonText.ToLower() ?? "");
            Assert.True(score >= 0.0 && score <= 100.0);
        }
    }
}
