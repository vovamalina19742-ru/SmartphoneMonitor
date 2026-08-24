using System;
using System.Collections.Generic;
using System.Linq;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    /// <summary>
    /// Advanced price analysis with dynamic ranges, confidence scoring,
    /// outlier detection, price tiers, histograms, and trend analysis.
    /// </summary>
    public class PriceAnalysisService
    {
        private readonly DatabaseService? _dbService;

        /// <summary>
        /// Default constructor (no database connectivity — trends unavailable).
        /// </summary>
        public PriceAnalysisService() { }

        /// <summary>
        /// DI constructor — enables price trend analysis via PriceHistory.
        /// </summary>
        public PriceAnalysisService(DatabaseService dbService)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        }

        public PriceAnalysisResult Analyze(List<Listing> privateListings)
        {
            var result = new PriceAnalysisResult();
            if (privateListings == null || privateListings.Count == 0)
                return result;

            // ── Phase 1: Dynamic price range (1st – 99th percentile) ──
            var allPrices = privateListings
                .Where(l => l.PriceValue > 0m)
                .Select(l => l.PriceValue)
                .OrderBy(p => p)
                .ToList();

            if (allPrices.Count == 0)
                return result;

            var (priceLow, priceHigh) = GetDynamicRange(allPrices);
            result.PriceLowPercentile = priceLow;
            result.PriceHighPercentile = priceHigh;

            var filtered = privateListings
                .Where(l => l.PriceValue >= priceLow && l.PriceValue <= priceHigh)
                .ToList();

            if (filtered.Count == 0)
                filtered = privateListings; // fallback

            // ── Phase 2: Global statistics ──
            var sortedFiltered = filtered.OrderBy(l => l.PriceValue).ToList();
            var fp = sortedFiltered.Select(l => l.PriceValue).ToList();

            result.MinPrice = fp.First();
            result.MaxPrice = fp.Last();
            result.AveragePrice = Math.Round(fp.Average(), 0);
            result.MedianPrice = GetMedian(fp);
            result.ListingsConsidered = filtered.Count;
            result.TotalListings = privateListings.Count;

            // ── Phase 3: Price histogram (dynamic buckets) ──
            result.PriceBuckets = BuildPriceBuckets(fp);

            // ── Phase 4: Brand stats with IQR, confidence, tier ──
            int validCount = filtered.Count;
            var brandGroups = filtered
                .GroupBy(l => l.Brand)
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var group in brandGroups)
            {
                var sorted = group.OrderBy(l => l.PriceValue).ToList();
                var prices = sorted.Select(l => l.PriceValue).ToList();
                var filteredPrices = FilterOutliersIQR(prices);
                decimal median = GetMedian(filteredPrices);
                decimal avg = filteredPrices.Count > 0
                    ? Math.Round(filteredPrices.Average(), 0)
                    : 0m;

                (string confidence, string tier) = ClassifyPriceStats(median, group.Count(), avg, filteredPrices);

                result.BrandStats.Add(new BrandStat
                {
                    Brand = group.Key,
                    Count = group.Count(),
                    Percent = Math.Round((double)group.Count() * 100.0 / validCount, 1),
                    MinPrice = filteredPrices.Count > 0
                        ? filteredPrices.First()
                        : (sorted.Count > 0 ? sorted.First().PriceValue : 0m),
                    MaxPrice = filteredPrices.Count > 0
                        ? filteredPrices.Last()
                        : (sorted.Count > 0 ? sorted.Last().PriceValue : 0m),
                    MedianPrice = median,
                    AveragePrice = avg,
                    Confidence = confidence,
                    Tier = tier,
                    StdDev = filteredPrices.Count >= 2
                        ? (decimal)Math.Round(StdDev(filteredPrices), 0)
                        : 0m
                });
            }

            // ── Phase 5: Exact model prices (Brand + Model + Storage + IsNew) ──
            result.ExactModelPrices = filtered
                .GroupBy(l => new ExactModelKey { Brand = l.Brand, Model = l.Model, StorageGB = l.StorageGB, IsNew = l.IsNew })
                .ToDictionary(g => g.Key, g =>
                {
                    var prices = g.Select(l => l.PriceValue).ToList();
                    var filteredIqr = FilterOutliersIQR(prices);
                    decimal med = GetMedian(filteredIqr);
                    string conf = filteredIqr.Count >= 5 ? "High" : filteredIqr.Count >= 2 ? "Medium" : "Low";
                    return new ModelPriceInfo
                    {
                        MedianPrice = Math.Round(med, 0),
                        Count = filteredIqr.Count,
                        TotalCount = g.Count(),
                        Confidence = conf,
                        OutliersRemoved = g.Count() - filteredIqr.Count
                    };
                });

            // ── Phase 6: General model prices (Brand + Model + IsNew) ──
            result.GeneralModelPrices = filtered
                .GroupBy(l => new GeneralModelKey { Brand = l.Brand, Model = l.Model, IsNew = l.IsNew })
                .ToDictionary(g => g.Key, g =>
                {
                    var prices = g.Select(l => l.PriceValue).ToList();
                    var filteredIqr = FilterOutliersIQR(prices);
                    decimal med = GetMedian(filteredIqr);
                    string conf = filteredIqr.Count >= 5 ? "High" : filteredIqr.Count >= 2 ? "Medium" : "Low";
                    return new ModelPriceInfo
                    {
                        MedianPrice = Math.Round(med, 0),
                        Count = filteredIqr.Count,
                        TotalCount = g.Count(),
                        Confidence = conf,
                        OutliersRemoved = g.Count() - filteredIqr.Count
                    };
                });

            // ── Phase 7: Price trends (7d / 30d) ──
            if (_dbService != null)
            {
                BuildPriceTrends(result, filtered);
            }

            return result;
        }

        /// <summary>
        /// Analyze price history from DB and compute trend indicators for brands.
        /// </summary>
        private void BuildPriceTrends(PriceAnalysisResult result, List<Listing> listings)
        {
            if (_dbService == null) return;

            var now = DateTime.UtcNow;
            var trendBrands = new Dictionary<string, PriceTrend>(StringComparer.OrdinalIgnoreCase);
            var brandModelStorage = listings
                .Where(l => !string.IsNullOrEmpty(l.Brand))
                .GroupBy(l => (brand: l.Brand, model: l.Model, storage: l.StorageGB))
                .ToList();

            foreach (var key in brandModelStorage)
            {
                try
                {
                    var history = _dbService.GetPriceHistoryForBrandAndModel(
                        key.Key.brand, key.Key.model, key.Key.storage);

                    if (history.Count < 2) continue;

                    var sorted = history.OrderBy(h => h.date).ToList();
                    int totalDays = (int)(sorted.Last().date - sorted.First().date).TotalDays;
                    if (totalDays < 1) continue;

                    // 7-day trend
                    var weekAgo = now.AddDays(-7);
                    var weekEntries = sorted.Where(h => h.date >= weekAgo).ToList();
                    if (weekEntries.Count >= 2)
                    {
                        var weekAvg1 = sorted.TakeLast(Math.Min(weekEntries.Count, sorted.Count - weekEntries.Count))
                            .Average(h => (double)h.price);
                        var weekAvg2 = weekEntries.Average(h => (double)h.price);
                        double weekChange = weekAvg1 > 0
                            ? Math.Round((weekAvg2 - weekAvg1) / weekAvg1 * 100.0, 1)
                            : 0.0;

                        string direction = weekChange switch
                        {
                            > 3.0 => "up",
                            < -3.0 => "down",
                            _ => "stable"
                        };

                        if (!trendBrands.ContainsKey(key.Key.brand))
                        {
                            trendBrands[key.Key.brand] = new PriceTrend();
                        }

                        var brandTrend = trendBrands[key.Key.brand];
                        brandTrend.SampleCount += weekEntries.Count;
                        brandTrend.WeekChangePercent += weekChange;

                        // accumulate directions — use last model's direction as tiebreaker
                        brandTrend.Direction7d = direction;
                        brandTrend.WeekPriceAvg = Math.Round((decimal)weekEntries.Average(h => (double)h.price), 0);
                    }

                    // 30-day trend
                    var monthAgo = now.AddDays(-30);
                    var monthEntries = sorted.Where(h => h.date >= monthAgo).ToList();
                    if (monthEntries.Count >= 2)
                    {
                        var monthAvg1 = sorted.Take(Math.Max(0, sorted.Count - monthEntries.Count))
                            .Select(h => (double)h.price).DefaultIfEmpty(0).Average();
                        var monthAvg2 = monthEntries.Average(h => (double)h.price);
                        double monthChange = monthAvg1 > 0
                            ? Math.Round((monthAvg2 - monthAvg1) / monthAvg1 * 100.0, 1)
                            : 0.0;

                        string dir30 = monthChange switch
                        {
                            > 5.0 => "up",
                            < -5.0 => "down",
                            _ => "stable"
                        };

                        if (!trendBrands.ContainsKey(key.Key.brand))
                        {
                            trendBrands[key.Key.brand] = new PriceTrend();
                        }

                        var bt = trendBrands[key.Key.brand];
                        bt.MonthChangePercent += monthChange;
                        bt.Direction30d = dir30;
                        bt.MonthPriceAvg = Math.Round((decimal)monthEntries.Average(h => (double)h.price), 0);
                    }
                }
                catch
                {
                    // skip if DB query fails (e.g., schema mismatch)
                }
            }

            result.BrandTrends = trendBrands;
        }

        /// <summary>
        /// Dynamic percentile range: [1st percentile, 99th percentile] clamped to reasonable bounds.
        /// </summary>
        public static (decimal low, decimal high) GetDynamicRange(List<decimal> sortedPrices, double lowPercentile = 0.01, double highPercentile = 0.99)
        {
            if (sortedPrices == null || sortedPrices.Count == 0)
                return (0m, 0m);

            if (sortedPrices.Count < 10)
            {
                // With very few samples, use min/max
                return (sortedPrices.Min(), sortedPrices.Max());
            }

            decimal low = GetPercentile(sortedPrices, lowPercentile);
            decimal high = GetPercentile(sortedPrices, highPercentile);

            // Clamp: low >= 200, high <= 20000 (avoid extreme edge cases)
            low = Math.Max(low, 200m);
            high = Math.Min(high, 20000m);

            // Ensure minimum spread
            if (high - low < 500m)
            {
                low = sortedPrices.Min();
                high = sortedPrices.Max();
            }

            return (low, high);
        }

        /// <summary>
        /// Build price histogram with dynamic bucket size.
        /// </summary>
        public static List<PriceBucket> BuildPriceBuckets(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0)
                return new List<PriceBucket>();

            var sorted = prices.OrderBy(p => p).ToList();
            decimal min = sorted.First();
            decimal max = sorted.Last();
            decimal range = max - min;

            // Adaptive bucket width: aim for ~8-12 buckets
            int bucketCount = Math.Clamp((int)Math.Sqrt(prices.Count), 5, 15);
            decimal bucketWidth = Math.Max(100m, Math.Round(range / bucketCount / 100m) * 100m);

            var buckets = new List<PriceBucket>();
            decimal currentLow = min;
            int idx = 0;
            int total = prices.Count;

            while (currentLow < max && idx < bucketCount)
            {
                decimal currentHigh = currentLow + bucketWidth;
                int count = sorted.Count(p => p >= currentLow && p < currentHigh);

                if (count > 0)
                {
                    var inBucket = sorted.Where(p => p >= currentLow && p < currentHigh).ToList();
                    decimal median = GetMedian(inBucket);

                    buckets.Add(new PriceBucket
                    {
                        Label = $"{currentLow:F0} — {currentHigh:F0} MDL",
                        Low = currentLow,
                        High = currentHigh,
                        Count = count,
                        Percent = Math.Round((double)count * 100.0 / total, 1),
                        MedianPrice = median
                    });
                }

                currentLow = currentHigh;
                idx++;
            }

            // Ensure last bucket includes max value
            if (buckets.Count > 0 && buckets.Last().High < max)
            {
                var last = buckets.Last();
                buckets[buckets.Count - 1] = new PriceBucket
                {
                    Label = $"{last.Low:F0} — {max:F0} MDL",
                    Low = last.Low,
                    High = max,
                    Count = sorted.Count(p => p >= last.Low),
                    Percent = Math.Round((double)sorted.Count(p => p >= last.Low) * 100.0 / total, 1),
                    MedianPrice = GetMedian(sorted.Where(p => p >= last.Low).ToList())
                };
            }

            return buckets;
        }

        /// <summary>
        /// Classify listing as outlier: Normal / Mild / Extreme.
        /// </summary>
        public static string ClassifyOutlier(decimal price, List<decimal> referencePrices)
        {
            if (referencePrices == null || referencePrices.Count < 4)
                return "Normal";

            var sorted = referencePrices.OrderBy(p => p).ToList();
            decimal q1 = GetPercentile(sorted, 0.25);
            decimal q3 = GetPercentile(sorted, 0.75);
            decimal iqr = q3 - q1;

            if (iqr == 0m) return "Normal";

            if (price < q1 - 3.0m * iqr || price > q3 + 3.0m * iqr)
                return "Extreme";
            if (price < q1 - 1.5m * iqr || price > q3 + 1.5m * iqr)
                return "Mild";

            return "Normal";
        }

        /// <summary>
        /// Classify price into Low/Medium/High confidence and Budget/MidRange/Premium/Flagship tier.
        /// </summary>
        public static (string confidence, string tier) ClassifyPriceStats(decimal medianPrice, int count, decimal avgPrice, List<decimal> filteredPrices)
        {
            string confidence = count switch
            {
                >= 10 => "High",
                >= 3 => "Medium",
                _ => "Low"
            };

            string tier = medianPrice switch
            {
                <= 1500m => "Budget",
                <= 3000m => "MidRange",
                <= 5000m => "Premium",
                _ => "Flagship"
            };

            return (confidence, tier);
        }

        public static List<decimal> FilterOutliersIQR(List<decimal> prices)
        {
            if (prices == null || prices.Count < 4)
                return prices ?? new List<decimal>();

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
                return sortedPrices[lowIndex];

            decimal lowValue = sortedPrices[lowIndex];
            decimal highValue = sortedPrices[highIndex];
            decimal weight = (decimal)(index - lowIndex);

            return lowValue + weight * (highValue - lowValue);
        }

        public static decimal GetMedian(List<decimal> sortedPrices)
        {
            if (sortedPrices == null || sortedPrices.Count == 0) return 0m;
            int count = sortedPrices.Count;
            int mid = count / 2;
            if (count % 2 == 0)
                return (sortedPrices[mid - 1] + sortedPrices[mid]) / 2m;
            return sortedPrices[mid];
        }

        private static double StdDev(List<decimal> values)
        {
            double avg = (double)values.Average();
            double sumSquares = values.Sum(v => Math.Pow((double)v - avg, 2));
            return Math.Sqrt(sumSquares / values.Count);
        }
    }

    // ── Key types ──

    public class ExactModelKey : IEquatable<ExactModelKey>
    {
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int StorageGB { get; set; }
        public bool IsNew { get; set; }

        public bool Equals(ExactModelKey? other)
        {
            if (other is null) return false;
            return Brand == other.Brand && Model == other.Model && StorageGB == other.StorageGB && IsNew == other.IsNew;
        }

        public override bool Equals(object? obj) => obj is ExactModelKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Brand, Model, StorageGB, IsNew);
    }

    public class GeneralModelKey : IEquatable<GeneralModelKey>
    {
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool IsNew { get; set; }

        public bool Equals(GeneralModelKey? other)
        {
            if (other is null) return false;
            return Brand == other.Brand && Model == other.Model && IsNew == other.IsNew;
        }

        public override bool Equals(object? obj) => obj is GeneralModelKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Brand, Model, IsNew);
    }

    public class PriceAnalysisResult
    {
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MedianPrice { get; set; }
        /// <summary>Number of listings that passed dynamic range filter and were used for stats.</summary>
        public int ListingsConsidered { get; set; }
        public int TotalListings { get; set; }

        /// <summary>1st percentile after dynamic range filtering.</summary>
        public decimal PriceLowPercentile { get; set; }
        /// <summary>99th percentile after dynamic range filtering.</summary>
        public decimal PriceHighPercentile { get; set; }

        public List<BrandStat> BrandStats { get; set; } = new();
        public List<PriceBucket> PriceBuckets { get; set; } = new();

        /// <summary>Dictionary: ExactModelKey -> ModelPriceInfo.</summary>
        public Dictionary<ExactModelKey, ModelPriceInfo> ExactModelPrices { get; set; } = new();
        /// <summary>Dictionary: GeneralModelKey -> ModelPriceInfo.</summary>
        public Dictionary<GeneralModelKey, ModelPriceInfo> GeneralModelPrices { get; set; } = new();

        /// <summary>Price trends per brand (7d / 30d) from PriceHistory table.</summary>
        public Dictionary<string, PriceTrend> BrandTrends { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class ModelPriceInfo
    {
        public decimal MedianPrice { get; set; }
        /// <summary>Count after IQR outlier removal.</summary>
        public int Count { get; set; }
        /// <summary>Original count before IQR filtering.</summary>
        public int TotalCount { get; set; }
        /// <summary>Low / Medium / High.</summary>
        public string Confidence { get; set; } = "Low";
        /// <summary>How many prices were removed as outliers.</summary>
        public int OutliersRemoved { get; set; }
    }

    /// <summary>
    /// A single bucket in the price histogram.
    /// </summary>
    public class PriceBucket
    {
        public string Label { get; set; } = string.Empty;
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public int Count { get; set; }
        public double Percent { get; set; }
        public decimal MedianPrice { get; set; }
    }

    /// <summary>
    /// Price trend indicators per brand (or model) computed from the PriceHistory table.
    /// </summary>
    public class PriceTrend
    {
        /// <summary>7-day price change percent (positive = up, negative = down).</summary>
        public double WeekChangePercent { get; set; }
        /// <summary>30-day price change percent.</summary>
        public double MonthChangePercent { get; set; }
        /// <summary>Direction: "up" / "down" / "stable".</summary>
        public string Direction7d { get; set; } = "stable";
        public string Direction30d { get; set; } = "stable";
        /// <summary>Number of price history samples used for trend calculation.</summary>
        public int SampleCount { get; set; }
        /// <summary>Average price in the last 7 days.</summary>
        public decimal WeekPriceAvg { get; set; }
        /// <summary>Average price in the last 30 days.</summary>
        public decimal MonthPriceAvg { get; set; }
    }
}