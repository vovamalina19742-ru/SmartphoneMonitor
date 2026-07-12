using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class DataAnalysisService
    {
        private static readonly string[] AppleModels = new string[32]
        {
            "15 pro max", "15 pro", "15 plus", "15", "14 pro max", "14 pro", "14 plus", "14", "13 pro max", "13 pro",
            "13 mini", "13", "12 pro max", "12 pro", "12 mini", "12", "11 pro max", "11 pro", "11", "xs max",
            "xs", "xr", "x", "se 2022", "se 2020", "se 3", "se 2", "se", "8 plus", "8",
            "7 plus", "7"
        };

        private static readonly string[] SamsungModels = new string[56]
        {
            "s24 ultra", "s24+", "s24", "s23 ultra", "s23+", "s23", "s22 ultra", "s22+", "s22", "s21 ultra",
            "s21 fe", "s21+", "s21", "s20 fe", "s20 ultra", "s20+", "s20", "note 20 ultra", "note 20", "note 10+",
            "note 10", "note 9", "a54", "a34", "a24", "a14", "a53", "a33", "a23", "a13",
            "a52s", "a52", "a72", "a32", "a22", "a12", "a51", "a71", "a31", "a21s",
            "a50", "a70", "a30", "a40", "a10", "a05", "a04", "a03", "m12", "m21",
            "m31", "m51", "m32", "m52", "z fold", "z flip"
        };

        private static readonly string[] XiaomiModels = new string[35]
        {
            "note 13 pro+", "note 13 pro", "note 13", "note 12 pro", "note 12", "note 11 pro", "note 11", "note 10 pro", "note 10s", "note 10",
            "note 9 pro", "note 9s", "note 9", "note 8 pro", "note 8", "13c", "12c", "10c", "9c", "12",
            "10", "9", "poco x6 pro", "poco x6", "poco x5 pro", "poco x5", "poco x4 pro", "poco x3 pro", "poco x3 nfc", "poco x3",
            "poco f5", "poco f4", "poco m5s", "poco m5", "poco m4"
        };

        private static readonly string[] GoogleModels = new string[14]
        {
            "pixel 8 pro", "pixel 8a", "pixel 8", "pixel 7 pro", "pixel 7a", "pixel 7", "pixel 6 pro", "pixel 6a", "pixel 6", "pixel 5a",
            "pixel 5", "pixel 4a", "pixel 4 xl", "pixel 4"
        };

        public AnalysisResult Analyze(List<Listing> listings, List<string> blacklist, Dictionary<string, decimal>? priceHistory = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var analysisResult = new AnalysisResult
            {
                TotalListings = listings.Count,
                AnalysisDate = DateTime.Now
            };

            var list = new List<Listing>();
            var list2 = new List<Listing>();
            int num = 0;

            foreach (var listing in listings)
            {
                string allText = (listing.Title + " " + listing.Description).ToLowerInvariant();
                bool flag = listing.IsCommercial || listing.SellerType == "Shop" || Constants.CommercialMarkers.Any((string m) => allText.Contains(m, StringComparison.OrdinalIgnoreCase));
                
                string phone = NormalizePhone(listing.PhoneNumber);
                if (!string.IsNullOrEmpty(phone) && blacklist.Any((string b) => NormalizePhone(b) == phone))
                {
                    flag = true;
                    num++;
                }

                listing.IsCommercial = flag;
                listing.IsUrgent = Constants.UrgencyMarkers.Any((string m) => allText.Contains(m, StringComparison.OrdinalIgnoreCase));
                
                if (flag)
                {
                    list2.Add(listing);
                }
                else
                {
                    list.Add(listing);
                }
            }

            analysisResult.PrivateListings = list.Count;
            analysisResult.CommercialListings = list2.Count;
            analysisResult.FilteredByBlacklist = num;

            // Chronic sellers
            var chronicGroups = from listing in listings
                                where !string.IsNullOrEmpty(listing.PhoneNumber)
                                group listing by NormalizePhone(listing.PhoneNumber) into g
                                where g.Count() >= 3
                                select g;

            foreach (var group in chronicGroups)
            {
                int uniqueBrands = group.Select((Listing l) => l.Brand).Distinct().Count();
                analysisResult.ChronicSellers.Add(new ChronicSeller
                {
                    PhoneNumber = group.Key,
                    SellerName = group.First().SellerName,
                    ListingCount = group.Count(),
                    UniqueBrands = uniqueBrands,
                    Reason = uniqueBrands >= 3 
                        ? $"Продаёт {uniqueBrands} разных бренда(ов), {group.Count()} объявлений" 
                        : $"Хронический продавец: {group.Count()} объявлений"
                });
            }

            // Calculations
            var priceRangeList = (from listing in list
                                  where listing.PriceValue >= 1000m && listing.PriceValue <= 5000m
                                  orderby listing.PriceValue
                                  select listing).ToList();

            if (priceRangeList.Count > 0)
            {
                analysisResult.MinPrice = priceRangeList.First().PriceValue;
                analysisResult.MaxPrice = priceRangeList.Last().PriceValue;
                analysisResult.AveragePrice = Math.Round(priceRangeList.Average((Listing l) => l.PriceValue), 0);
                int midIndex = priceRangeList.Count / 2;
                analysisResult.MedianPrice = priceRangeList.Count % 2 == 0 
                    ? (priceRangeList[midIndex - 1].PriceValue + priceRangeList[midIndex].PriceValue) / 2m 
                    : priceRangeList[midIndex].PriceValue;
            }

            int validPriceCount = list.Count((Listing l) => l.PriceValue >= 1000m && l.PriceValue <= 5000m);

            var brandGroups = from listing in list
                              where listing.PriceValue >= 1000m && listing.PriceValue <= 5000m
                              group listing by listing.Brand into g
                              orderby g.Count() descending
                              select g;

            foreach (var group in brandGroups)
            {
                var sorted = group.OrderBy((Listing l) => l.PriceValue).ToList();
                var prices = sorted.Select(l => l.PriceValue).ToList();
                var filteredPrices = FilterOutliersIQR(prices);
                
                decimal medianPrice = GetMedian(filteredPrices);

                analysisResult.BrandStats.Add(new BrandStat
                {
                    Brand = group.Key,
                    Count = group.Count(),
                    Percent = Math.Round((double)group.Count() * 100.0 / validPriceCount, 1),
                    MinPrice = filteredPrices.Count > 0 ? filteredPrices.First() : (sorted.Count > 0 ? sorted.First().PriceValue : 0m),
                    MaxPrice = filteredPrices.Count > 0 ? filteredPrices.Last() : (sorted.Count > 0 ? sorted.Last().PriceValue : 0m),
                    MedianPrice = medianPrice
                });
            }

            // Extract models, battery health, and defects
            foreach (var listing in list)
            {
                listing.Model = ExtractModel(listing.Title, listing.Brand);
                listing.BatteryHealth = ExtractBatteryHealth(listing.Title, listing.Description, listing.Brand);

                var defectResult = DetectDefectsAndEstimateRepair(listing.Title, listing.Description, listing.Brand, listing.Model, listing.BatteryHealth);
                listing.Defects = defectResult.defects;
                listing.RepairCost = defectResult.repairCost;
                listing.IsStolen = defectResult.isStolen;
            }

            // Deviation calculation groups with IQR-filtered Medians
            var exactModelPrices = list
                .Where(listing => listing.PriceValue >= 1000m && listing.PriceValue <= 5000m)
                .GroupBy(listing => new { listing.Brand, listing.Model, listing.StorageGB })
                .ToDictionary(g => g.Key, g => {
                    var prices = g.Select(l => l.PriceValue).ToList();
                    var filtered = FilterOutliersIQR(prices);
                    return new {
                        MedianPrice = Math.Round(GetMedian(filtered), 0),
                        Count = filtered.Count
                    };
                });

            var generalModelPrices = list
                .Where(listing => listing.PriceValue >= 1000m && listing.PriceValue <= 5000m)
                .GroupBy(listing => new { listing.Brand, listing.Model })
                .ToDictionary(g => g.Key, g => {
                    var prices = g.Select(l => l.PriceValue).ToList();
                    var filtered = FilterOutliersIQR(prices);
                    return new {
                        MedianPrice = Math.Round(GetMedian(filtered), 0),
                        Count = filtered.Count
                    };
                });

            foreach (var l in list)
            {
                decimal referencePrice = 0m;
                string valueLabel = "";

                var exactKey = new { l.Brand, l.Model, l.StorageGB };
                var generalKey = new { l.Brand, l.Model };

                if (l.StorageGB > 0 && exactModelPrices.TryGetValue(exactKey, out var exactVal) && exactVal.Count >= 2)
                {
                    referencePrice = exactVal.MedianPrice;
                    valueLabel = "модели c " + (l.StorageGB == 1024 ? "1 TB" : l.StorageGB + " GB");
                }
                else if (generalModelPrices.TryGetValue(generalKey, out var generalVal) && generalVal.Count >= 2)
                {
                    referencePrice = generalVal.MedianPrice;
                    valueLabel = "модели";
                }
                else
                {
                    var brandStat = analysisResult.BrandStats.FirstOrDefault((BrandStat b) => b.Brand == l.Brand);
                    if (brandStat != null && brandStat.Count >= 3)
                    {
                        referencePrice = brandStat.MedianPrice;
                        valueLabel = "бренда";
                    }
                    else
                    {
                        referencePrice = analysisResult.MedianPrice;
                        valueLabel = "рынка";
                    }
                }

                l.ModelAveragePrice = referencePrice;
                l.NetProfitMargin = l.IsStolen ? 0m : (referencePrice - (l.PriceValue + l.RepairCost));

                if (referencePrice > 0m)
                {
                    decimal priceDeviation = (referencePrice - l.PriceValue) / referencePrice * 100m;
                    l.ModelPriceDeviationPercent = Math.Round((double)priceDeviation, 1);

                    // Score calculation v2
                    double score = 50.0;
                    score += (double)priceDeviation * 2.0;

                    // Memory Size Bonus
                    if (l.StorageGB == 256) score += 5.0;
                    else if (l.StorageGB == 512) score += 10.0;
                    else if (l.StorageGB >= 1024) score += 15.0;

                    // Freshness
                    if (l.DaysOld == 0) score += 10.0;
                    else if (l.DaysOld == 1) score += 5.0;
                    else if (l.DaysOld > 3) score -= 5.0;

                    // Views
                    if (l.Views > 0)
                    {
                        if (l.Views < 50) score += 5.0;
                        else if (l.Views > 150) score -= 5.0;
                    }

                    // Urgency
                    if (l.IsUrgent) score += 5.0;

                    // Battery (Apple only)
                    if (l.Brand.Equals("Apple", StringComparison.OrdinalIgnoreCase) && l.BatteryHealth > 0)
                    {
                        if (l.BatteryHealth >= 90) score += 5.0;
                        else if (l.BatteryHealth < 80 && l.BatteryHealth >= 75) score -= 15.0;
                        else if (l.BatteryHealth < 75) score -= 25.0;
                    }

                    // Price drop check
                    if (priceHistory != null && priceHistory.TryGetValue(l.Url, out decimal prevPrice) && l.PriceValue < prevPrice)
                    {
                        score += 10.0;
                        decimal drop = prevPrice - l.PriceValue;
                        l.PriceDisplay = $"{l.PriceValue:F0} lei (📉 -{drop:F0} л)";
                    }
                    else
                    {
                        l.PriceDisplay = $"{l.PriceValue:F0} lei";
                    }

                    // Defects Penalty
                    if (l.RepairCost > 0m)
                    {
                        score -= 10.0;
                    }

                    // Stolen / Lock Hard Penalty
                    if (l.IsStolen)
                    {
                        score = 0.0;
                    }

                    score = Math.Max(0.0, Math.Min(100.0, score));

                    if (score >= 70.0 && !l.IsCommercial && !l.IsStolen)
                    {
                        analysisResult.HotDeals.Add(new HotDeal
                        {
                            RecommendationScore = score,
                            Title = $"⭐ [Рекомендация: {score:F0}/100] " + l.Title,
                            Brand = l.Brand,
                            PriceValue = l.PriceValue,
                            BrandMedian = referencePrice,
                            DiscountPercent = Math.Round((double)priceDeviation, 1),
                            Url = l.Url,
                            PhoneNumber = l.PhoneNumber,
                            SellerName = l.SellerName,
                            Views = l.Views,
                            StorageGB = l.StorageGB,
                            DaysOld = l.DaysOld,
                            PostedDate = l.PostedDate,
                            BatteryHealth = l.BatteryHealth,
                            Defects = l.Defects,
                            RepairCost = l.RepairCost,
                            NetProfitMargin = l.NetProfitMargin,
                            IsStolen = l.IsStolen
                        });
                    }

                    if (priceDeviation >= 5m)
                    {
                        l.ComparisonText = $"Дешевле {valueLabel} на {Math.Abs(priceDeviation):F0}%";
                        l.ComparisonColor = "#2E7D32";
                        l.ComparisonBg = "#E8F5E9";
                    }
                    else if (priceDeviation <= -5m)
                    {
                        l.ComparisonText = $"Дороже {valueLabel} на {Math.Abs(priceDeviation):F0}%";
                        l.ComparisonColor = "#C62828";
                        l.ComparisonBg = "#FFEBEE";
                    }
                    else
                    {
                        l.ComparisonText = $"Около {valueLabel} ({((priceDeviation >= 0m) ? "-" : "+")}{Math.Abs(priceDeviation):F0}%)";
                        l.ComparisonColor = "#455A64";
                        l.ComparisonBg = "#ECEFF1";
                    }
                }
                else
                {
                    l.PriceDisplay = l.PriceValue > 0m ? $"{l.PriceValue:F0} lei" : "";
                    l.ComparisonText = "Нет цены";
                    l.ComparisonColor = "#777777";
                    l.ComparisonBg = "#F5F5F5";
                }
            }

            analysisResult.AllPrivateListings = list;
            analysisResult.HotDeals = analysisResult.HotDeals.OrderByDescending((HotDeal h) => h.RecommendationScore).ToList();
            
            stopwatch.Stop();
            analysisResult.AnalysisDuration = stopwatch.Elapsed;
            return analysisResult;
        }

        private static string ExtractModel(string title, string brand)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Другой";
            }
            string text = title.ToLowerInvariant();
            string[]? array = brand.ToLowerInvariant() switch
            {
                "apple" => AppleModels,
                "samsung" => SamsungModels,
                "xiaomi" => XiaomiModels,
                "google" => GoogleModels,
                _ => null
            };

            if (array != null)
            {
                foreach (string text2 in array)
                {
                    string pattern = "\\b" + Regex.Escape(text2) + "\\b";
                    if (!Regex.IsMatch(text, pattern))
                    {
                        continue;
                    }
                    string[] array3 = text2.Split(' ');
                    for (int j = 0; j < array3.Length; j++)
                    {
                        if (array3[j].Length > 0)
                        {
                            array3[j] = char.ToUpper(array3[j][0]) + array3[j].Substring(1);
                        }
                    }
                    string text3 = string.Join(" ", array3);
                    if (brand.Equals("apple", StringComparison.OrdinalIgnoreCase))
                    {
                        return "iPhone " + text3;
                    }
                    if (brand.Equals("samsung", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Galaxy " + text3;
                    }
                    return text3;
                }
            }

            string text4 = text;
            string text5 = brand.ToLowerInvariant();
            int num = text4.IndexOf(text5);
            if (num >= 0)
            {
                text4 = text4.Substring(num + text5.Length).Trim();
            }
            text4 = Regex.Replace(text4, "\\b(telefon|smartfon|telefoane|оригинал|original)\\b", "", RegexOptions.IgnoreCase).Trim();
            var match = Regex.Match(text4, "^([a-zA-Z0-9+-]{2,})\\s*([a-zA-Z0-9+-]{2,})?");
            if (match.Success)
            {
                string value = match.Groups[1].Value;
                string value2 = match.Groups[2].Value;
                value = char.ToUpper(value[0]) + value.Substring(1).ToLowerInvariant();
                if (!string.IsNullOrEmpty(value2) && value2.Length >= 2)
                {
                    value2 = char.ToUpper(value2[0]) + value2.Substring(1).ToLowerInvariant();
                    return value + " " + value2;
                }
                return value;
            }
            return "Другой";
        }

        private static int ExtractBatteryHealth(string title, string description, string brand)
        {
            if (!brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
            string text = (title + " " + description).ToLowerInvariant();
            var matches = Regex.Matches(text, @"(?:акб|battery|батаре|health|аккум|состояни|износ)\D*?(\d{2,3})\s*%?");
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int val))
                {
                    bool isWear = m.Value.Contains("износ");
                    if (isWear)
                    {
                        if (val > 0 && val < 50)
                        {
                            val = 100 - val;
                        }
                    }
                    if (val >= 50 && val <= 100)
                    {
                        return val;
                    }
                }
            }

            var percentMatches = Regex.Matches(text, @"\b(\d{2})%");
            foreach (Match m in percentMatches)
            {
                if (int.TryParse(m.Groups[1].Value, out int val) && val >= 50 && val <= 100)
                {
                    return val;
                }
            }
            return 0;
        }

        public static string NormalizePhone(string? phone)
        {
            if (string.IsNullOrEmpty(phone))
            {
                return string.Empty;
            }
            string text = Regex.Replace(phone, "[^\\d]", "");
            if (text.StartsWith("373"))
            {
                text = text.Substring(3);
            }
            if (text.StartsWith("0"))
            {
                text = text.Substring(1);
            }
            return text;
        }

        private static List<decimal> FilterOutliersIQR(List<decimal> prices)
        {
            if (prices == null || prices.Count < 4)
            {
                return prices ?? new List<decimal>();
            }

            var sorted = prices.OrderBy(p => p).ToList();
            
            decimal q1 = GetPercentile(sorted, 0.25);
            decimal q3 = GetPercentile(sorted, 0.75);
            decimal iqr = q3 - q1;

            decimal lowLimit = q1 - 1.5m * iqr;
            decimal highLimit = q3 + 1.5m * iqr;

            return sorted.Where(p => p >= lowLimit && p <= highLimit).ToList();
        }

        private static decimal GetPercentile(List<decimal> sortedPrices, double percentile)
        {
            double count = sortedPrices.Count;
            double index = percentile * (count - 1);
            int lowIndex = (int)Math.Floor(index);
            int highIndex = (int)Math.Ceiling(index);
            
            if (lowIndex == highIndex)
            {
                return sortedPrices[lowIndex];
            }

            decimal lowValue = sortedPrices[lowIndex];
            decimal highValue = sortedPrices[highIndex];
            decimal weight = (decimal)(index - lowIndex);

            return lowValue + weight * (highValue - lowValue);
        }

        private static decimal GetMedian(List<decimal> sortedPrices)
        {
            if (sortedPrices == null || sortedPrices.Count == 0) return 0m;
            int count = sortedPrices.Count;
            int mid = count / 2;
            if (count % 2 == 0)
            {
                return (sortedPrices[mid - 1] + sortedPrices[mid]) / 2m;
            }
            return sortedPrices[mid];
        }

        public static (List<string> defects, decimal repairCost, bool isStolen) DetectDefectsAndEstimateRepair(string title, string description, string brand, string model, int batteryHealth)
        {
            var defects = new List<string>();
            decimal repairCost = 0m;
            bool isStolen = false;

            string text = (title + " " + description).ToLowerInvariant();

            // 1. iCloud / MDM / Lock / Stolen checks
            if (Regex.IsMatch(text, @"\b(icloud|блокировк|заблокир|mdm|activation lock|bypass|активац|на запчаст|запчаст|piese)\b"))
            {
                defects.Add("Блокировка / На запчасти");
                isStolen = true;
            }

            // 2. Screen cracks / damage
            if (Regex.IsMatch(text, @"\b(разбит|трещин|битый|скол|broken|cracked|spart|fisur|crăpat|crapat|ecran spart)\b") && 
                !Regex.IsMatch(text, @"\b(пленк|стекл.*защит|стекло защит|защитн.*стекл|pelicul|sticla.*prot)\b"))
            {
                defects.Add("Разбит экран / стекло");
                decimal screenCost = 1500m;
                if (brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Contains("Pro") || model.Contains("Max"))
                    {
                        screenCost = 2500m;
                    }
                }
                else
                {
                    if (model.Contains("Ultra") || model.Contains("Fold") || model.Contains("Flip") || model.Contains("S20") || model.Contains("S21") || model.Contains("S22") || model.Contains("S23") || model.Contains("S24"))
                    {
                        screenCost = 2000m;
                    }
                    else
                    {
                        screenCost = 1000m;
                    }
                }
                repairCost += screenCost;
            }

            // 3. FaceID / TouchID / Sensors
            if (Regex.IsMatch(text, @"\b(faceid|face id|touchid|touch id|отпечат|сканер|распознаван)\b") && 
                Regex.IsMatch(text, @"\b(не.*работ|ошибк|defect|неактив|не актив|nu func|nu luc)\b"))
            {
                defects.Add("Не работает FaceID/TouchID");
                repairCost += 800m;
            }

            // 4. Battery Health
            if (brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
            {
                if (batteryHealth > 0 && batteryHealth < 80)
                {
                    defects.Add($"Износ АКБ ({batteryHealth}%)");
                    repairCost += 400m;
                }
                else if (Regex.IsMatch(text, @"\b(менять акб|замена акб|батаре.*плох|замена батаре|акб.*сервис)\b"))
                {
                    defects.Add("Сервис АКБ / Требуется замена");
                    repairCost += 400m;
                }
            }

            // 5. Back glass / cover damage
            if (Regex.IsMatch(text, @"\b(задн.*крышк|задн.*стекл|корпус.*разбит|capac spate|sticla spate)\b"))
            {
                defects.Add("Разбито заднее стекло / крышка");
                repairCost += 500m;
            }

            // 6. Camera issues
            if (Regex.IsMatch(text, @"\b(камер.*не.*работ|камер.*разбит|пятн.*камер|линз.*разбит|focus.*не|фокус.*не)\b"))
            {
                defects.Add("Дефект камеры");
                repairCost += 600m;
            }

            return (defects, repairCost, isStolen);
        }
    }
}
