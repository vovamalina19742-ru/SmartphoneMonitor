using System;
using System.Collections.Generic;
using System.Diagnostics;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;

namespace SmartphoneMonitor.Tests
{
    public static class PromoMatchingTests
    {
        // Simple manual test runner. Call from debugger or integrate into test runner later.
        public static void Run()
        {
            var promoService = new RetailPromoService();

            var promos = new List<RetailPromoPrice>
            {
                new RetailPromoPrice { Shop = "ShopA", Brand = "Apple", Name = "iPhone 14 Pro Max", StorageGB = 256, Price = 25000m },
                new RetailPromoPrice { Shop = "ShopB", Brand = "Apple", Name = "iPhone 14", StorageGB = 128, Price = 18000m },
                new RetailPromoPrice { Shop = "ShopC", Brand = "Samsung", Name = "Galaxy S23", StorageGB = 256, Price = 17000m }
            };

            var map = promoService.BuildPromoMap(promos);

            var listing = new Listing
            {
                Brand = "Apple",
                Title = "Apple iPhone 14 Pro Max 256 GB",
                Model = "iPhone 14 Pro Max",
                StorageGB = 256,
                PriceValue = 24000m,
                IsNew = true
            };

            var eval = new ListingEvaluationService();
            eval.ApplyComparisonText(listing, 0m, "новых моделей", map);

            Debug.WriteLine($"ComparisonText: {listing.ComparisonText}");
            Debug.WriteLine($"ComparisonColor: {listing.ComparisonColor}");

            // Basic checks (write results)
            bool found = !string.IsNullOrEmpty(listing.ComparisonText) && (listing.ComparisonText.Contains("ShopA") || listing.ComparisonText.Contains("Дешевле") || listing.ComparisonText.Contains("Дороже"));
            Debug.WriteLine($"Promo matching test passed: {found}");
        }
    }
}
