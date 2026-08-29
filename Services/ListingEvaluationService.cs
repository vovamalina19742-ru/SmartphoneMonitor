using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class ListingEvaluationService
    {
        // Public wrapper to expose best promo lookup for metrics/tests
        public RetailPromoPrice? GetBestPromo(Listing listing, IDictionary<string, List<RetailPromoPrice>> retailPromoMap)
        {
            return FindBestPromo(listing, retailPromoMap);
        }

        public double EvaluateScore(Listing listing, decimal priceDeviation, decimal referencePrice, IDictionary<string, List<RetailPromoPrice>> retailPromoMap, Dictionary<string, decimal>? priceHistory)
        {
            if (ListingClassifier.IsBuyAd(listing.Title, listing.Description))
            {
                return 0.0;
            }

            if (ListingClassifier.IsFeatureOrRetroPhone(listing.Title, listing.Brand))
            {
                return 0.0;
            }

            if (ListingClassifier.IsAppleOrIPhone(listing.Title, listing.Brand))
            {
                return 0.0;
            }

            double score = 50.0;
            score += (double)priceDeviation * 2.0;

            var promo = FindBestPromo(listing, retailPromoMap);
            if (promo != null)
            {
                if (listing.IsNew)
                {
                    score += listing.PriceValue > promo.Price ? -30.0 : 10.0;
                }
                else if (listing.PriceValue >= promo.Price * 0.9m)
                {
                    score -= 15.0;
                }
            }

            score += GetStorageScore(listing.StorageGB);
            score += GetFreshnessScore(listing.DaysOld);
            score += GetViewsScore(listing.Views);
            score += listing.IsUrgent ? 5.0 : 0.0;
            score += GetBatteryHealthScore(listing.Brand, listing.BatteryHealth);

            // 1. Hard Penalty: Reseller / Commercial showcase
            bool isReseller = listing.SellerType.Equals("RESELLER", StringComparison.OrdinalIgnoreCase) ||
                              listing.IsCommercial ||
                              listing.NewSmartphoneCategory == "Reseller";
            if (isReseller)
            {
                score -= 50.0;
            }
            else if (listing.SellerType.Equals("FRESH_PRIVATE", StringComparison.OrdinalIgnoreCase) || listing.NewSmartphoneCategory == "FreshPrivate")
            {
                score += 5.0; // Trust bonus for fresh private seller (real owner, high discount potential)
            }

            // 2. Hard Penalty: Device Age
            int ageYears = ModelPriceBaselineService.EstimateDeviceAgeYears(listing.Brand, listing.Model, listing.Title);
            if (ageYears >= 5)
            {
                score -= 25.0; // Penalty for 5+ years old (EOL, hardware wear)
            }
            else if (ageYears >= 3)
            {
                score -= 15.0; // Penalty for 3-4 years old
            }

            var osService = new XiaomiOsSupportService();
            var osSupport = osService.AnalyzeModel(listing.Title);
            if (osSupport.IsXiaomiDevice)
            {
                if (osSupport.SupportsAndroid17)
                {
                    score += 5.0; // Bonus for device longevity
                }
                else if (!osSupport.SupportsAndroid16)
                {
                    score -= 15.0; // Penalty for EOL
                }
            }

            if (priceHistory != null && priceHistory.TryGetValue(listing.Url, out var prevPrice) && listing.PriceValue < prevPrice)
            {
                score += 10.0;
                var drop = prevPrice - listing.PriceValue;
                listing.PriceDisplay = $"{listing.PriceValue:F0} lei (📉 -{drop:F0} л)";
            }
            else
            {
                listing.PriceDisplay = $"{listing.PriceValue:F0} lei";
            }

            bool hasSevereHardwareDefect = false;
            bool hasRepairsOrNonOriginalParts = false;

            if (listing.Defects.Count > 0)
            {
                foreach (var defect in listing.Defects)
                {
                    if (defect.Contains("FaceID") || defect.Contains("Amprenta"))
                    {
                        score -= 25.0;
                    }
                    else if (defect.Contains("Заменен дисплей") || defect.Contains("schimbat"))
                    {
                        score -= 20.0;
                        hasRepairsOrNonOriginalParts = true;
                    }
                    else if (defect.Contains("задняя крышка") || defect.Contains("capac"))
                    {
                        score -= 15.0; // Cosmetic repairable defect (not severe!)
                    }
                    else if (defect.Contains("АКБ") || defect.Contains("baterie"))
                    {
                        score -= 10.0; // Battery wear / replacement
                    }
                    else if (defect.Contains("дисплей") || defect.Contains("ecran") || defect.Contains("стекло (ecran"))
                    {
                        score -= 40.0;
                        hasSevereHardwareDefect = true;
                    }
                    else if (defect.Contains("Серьёзные проблемы") || defect.Contains("Дефект камеры"))
                    {
                        score -= 35.0;
                        hasSevereHardwareDefect = true;
                    }
                    else if (defect.Contains("копия") || defect.Contains("clone"))
                    {
                        score -= 50.0;
                        hasSevereHardwareDefect = true;
                    }
                    else
                    {
                        score -= 10.0; // Generic penalty
                    }
                }

                if (hasRepairsOrNonOriginalParts)
                {
                    if (score > 60.0) score = 60.0; // Hard cap for repairs
                }

                if (hasSevereHardwareDefect)
                {
                    if (score > 30.0) score = 30.0; // Hard cap for severe risk / broken screen
                    listing.HasCriticalDefect = true;
                }
            }

            if (listing.RepairCost > 0m)
            {
                score -= (double)(listing.RepairCost / 100m) * 2.0;
                if (listing.RepairCost >= 1500m)
                {
                    listing.HasCriticalDefect = true;
                    if (score > 25.0) score = 25.0;
                }
                else if (!hasSevereHardwareDefect && !hasRepairsOrNonOriginalParts)
                {
                    if (score > 60.0) score = 60.0;
                }
            }

            if (listing.IsStolen)
            {
                score = 0.0;
            }
            else if (listing.HasCriticalDefect)
            {
                if (score > 25.0) score = 25.0; // Critical defect caps at 25
            }
            else if (isReseller)
            {
                if (score > 40.0) score = 40.0; // Resellers hard capped at 40
            }
            else if (ageYears >= 5)
            {
                if (score > 65.0) score = 65.0; // 5+ year old devices hard capped at 65
            }

            return Math.Max(0.0, Math.Min(100.0, score));
        }

        public void ApplyComparisonText(Listing listing, decimal priceDeviation, string valueLabel, IDictionary<string, List<RetailPromoPrice>> retailPromoMap)
        {
            var promo = FindBestPromo(listing, retailPromoMap);
            if (promo != null)
            {
                if (listing.IsNew)
                {
                    ApplyNewComparison(listing, promo);
                    return;
                }

                if (listing.PriceValue >= promo.Price * 0.9m)
                {
                    listing.ComparisonText = $"⚠️ Всего на {(promo.Price - listing.PriceValue):F0} MDL дешевле НОВОГО в {promo.Shop} по акции!";
                    listing.ComparisonColor = "#E65100";
                    listing.ComparisonBg = "#FFF3E0";
                    return;
                }

                // For used phones with price below 90% of new promo — apply regular comparison
                ApplyDefaultComparison(listing, priceDeviation, valueLabel);
                return;
            }

            ApplyDefaultComparison(listing, priceDeviation, valueLabel);
        }

        private static RetailPromoPrice? FindBestPromo(Listing listing, IDictionary<string, List<RetailPromoPrice>> retailPromoMap)
        {
            // Try exact match: Brand_Model_Storage
                string exactKey = PromoKeyHelper.SanitizeKey(listing.Brand, listing.Model, listing.StorageGB);
            if (retailPromoMap.TryGetValue(exactKey, out var matchingPromos) && matchingPromos.Count > 0)
            {
                var best = matchingPromos.OrderBy(rp => rp.Price).First();
                Log.Debug("Promo exact match: {Key} -> {Shop} {Price}", exactKey, best.Shop, best.Price);
                return best;
            }

            // Try general model match without storage: Brand_Model_0 or Brand_Model
                string generalKey = PromoKeyHelper.SanitizeKey(listing.Brand, listing.Model, 0);
            if (retailPromoMap.TryGetValue(generalKey, out matchingPromos) && matchingPromos.Count > 0)
            {
                var best = matchingPromos.OrderBy(rp => rp.Price).First();
                Log.Debug("Promo general match: {Key} -> {Shop} {Price}", generalKey, best.Shop, best.Price);
                return best;
            }

                generalKey = PromoKeyHelper.SanitizeKey(listing.Brand, listing.Model);
            if (retailPromoMap.TryGetValue(generalKey, out matchingPromos) && matchingPromos.Count > 0)
            {
                return matchingPromos.OrderBy(rp => rp.Price).First();
            }

            // Fallback: any promo entries for the same brand
                var brandPrefix = PromoKeyHelper.SanitizePrefix(listing.Brand);
                var brandMatches = retailPromoMap
                    .Where(kv => kv.Key.StartsWith(brandPrefix, StringComparison.OrdinalIgnoreCase))
                .SelectMany(kv => kv.Value)
                .ToList();
            if (brandMatches.Count > 0)
            {
                var best = brandMatches.OrderBy(rp => rp.Price).First();
                Log.Debug("Promo brand fallback: {Brand} -> {Shop} {Price}", listing.Brand, best.Shop, best.Price);
                return best;
            }

            return null;
        }

        private static double GetStorageScore(int storageGB)
        {
            return storageGB switch
            {
                256 => 5.0,
                512 => 10.0,
                >= 1024 => 15.0,
                _ => 0.0
            };
        }

        private static double GetFreshnessScore(int daysOld)
        {
            return daysOld switch
            {
                0 => 10.0,
                1 => 5.0,
                > 3 => -5.0,
                _ => 0.0
            };
        }

        private static double GetViewsScore(int views)
        {
            if (views <= 0) return 0.0;
            if (views < 50) return 5.0;
            if (views > 150) return -5.0;
            return 0.0;
        }

        private static double GetBatteryHealthScore(string brand, int batteryHealth)
        {
            if (!brand.Equals("Apple", StringComparison.OrdinalIgnoreCase) || batteryHealth <= 0)
            {
                return 0.0;
            }

            // Order matters: check from worst to best to avoid shadowed conditions
            if (batteryHealth < 75) return -25.0;
            if (batteryHealth < 80) return -15.0;  // 75-79
            if (batteryHealth >= 90) return 5.0;   // 90-100
            return 0.0;                             // 80-89
        }

        private static void ApplyNewComparison(Listing listing, RetailPromoPrice promo)
        {
            if (listing.PriceValue > promo.Price)
            {
                listing.ComparisonText = $"❌ Дороже на {(listing.PriceValue - promo.Price):F0} MDL, чем в {promo.Shop} по акции ({promo.Price:F0} MDL)";
                listing.ComparisonColor = "#C62828";
                listing.ComparisonBg = "#FFEBEE";
            }
            else
            {
                var savings = promo.Price - listing.PriceValue;
                listing.ComparisonText = $"🔥 Дешевле на {savings:F0} MDL, чем в {promo.Shop} по акции ({promo.Price:F0} MDL)";
                listing.ComparisonColor = "#2E7D32";
                listing.ComparisonBg = "#E8F5E9";
            }
        }

        private static void ApplyDefaultComparison(Listing listing, decimal priceDeviation, string valueLabel)
        {
            if (priceDeviation >= 5m)
            {
                listing.ComparisonText = $"Дешевле {valueLabel} на {Math.Abs(priceDeviation):F0}%";
                listing.ComparisonColor = "#2E7D32";
                listing.ComparisonBg = "#E8F5E9";
            }
            else if (priceDeviation <= -5m)
            {
                listing.ComparisonText = $"Дороже {valueLabel} на {Math.Abs(priceDeviation):F0}%";
                listing.ComparisonColor = "#C62828";
                listing.ComparisonBg = "#FFEBEE";
            }
            else
            {
                listing.ComparisonText = $"Около {valueLabel} ({((priceDeviation >= 0m) ? "-" : "+")}{Math.Abs(priceDeviation):F0}%)";
                listing.ComparisonColor = "#455A64";
                listing.ComparisonBg = "#ECEFF1";
            }
        }
    }
}
