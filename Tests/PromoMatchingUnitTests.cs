using System.Collections.Generic;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;
using Xunit;

namespace SmartphoneMonitor.Tests
{
    public class PromoMatchingUnitTests
    {
        [Fact]
        public void FindsExactPromo_ByStorage()
        {
            var promos = new List<RetailPromoPrice>
            {
                new RetailPromoPrice { Shop = "ShopA", Brand = "Apple", Name = "iPhone 14 Pro Max", StorageGB = 256, Price = 25000m },
                new RetailPromoPrice { Shop = "ShopB", Brand = "Apple", Name = "iPhone 14", StorageGB = 128, Price = 18000m }
            };

            var promoService = new RetailPromoService();
            var map = promoService.BuildPromoMap(promos);

            var listing = new Listing { Brand = "Apple", Model = "iPhone 14 Pro Max", StorageGB = 256, PriceValue = 24000m, IsNew = true };

            var eval = new ListingEvaluationService();
            var promo = typeof(ListingEvaluationService)
                .GetMethod("FindBestPromo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { listing, map }) as RetailPromoPrice;

            Assert.NotNull(promo);
            Assert.Equal("ShopA", promo.Shop);
        }

        [Fact]
        public void FindsGeneralPromo_WithoutStorage()
        {
            var promos = new List<RetailPromoPrice>
            {
                new RetailPromoPrice { Shop = "ShopB", Brand = "Apple", Name = "iPhone 14", StorageGB = 0, Price = 18000m }
            };

            var promoService = new RetailPromoService();
            var map = promoService.BuildPromoMap(promos);

            var listing = new Listing { Brand = "Apple", Model = "iPhone 14", StorageGB = 128, PriceValue = 17500m, IsNew = true };

            var promo = typeof(ListingEvaluationService)
                .GetMethod("FindBestPromo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { listing, map }) as RetailPromoPrice;

            Assert.NotNull(promo);
            Assert.Equal("ShopB", promo.Shop);
        }

        [Fact]
        public void FindsBrandFallback_WhenNoModelMatch()
        {
            var promos = new List<RetailPromoPrice>
            {
                new RetailPromoPrice { Shop = "ShopC", Brand = "Samsung", Name = "Any Galaxy Promo", StorageGB = 0, Price = 15000m }
            };

            var promoService = new RetailPromoService();
            var map = promoService.BuildPromoMap(promos);

            var listing = new Listing { Brand = "Samsung", Model = "Unknown Model X", StorageGB = 64, PriceValue = 14000m, IsNew = false };

            var promo = typeof(ListingEvaluationService)
                .GetMethod("FindBestPromo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { listing, map }) as RetailPromoPrice;

            Assert.NotNull(promo);
            Assert.Equal("ShopC", promo.Shop);
        }
    }
}
