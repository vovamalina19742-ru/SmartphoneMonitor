using System;
using System.Collections.Generic;
using System.Linq;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    /// <summary>
    /// Количественный финансово-аналитический движок (Vibe-Trading inspired)
    /// для оценки справедливой цены (Fair Value), ликвидности и арбитражной маржи
    /// исключительно на рынке Android смартфонов (Samsung, Xiaomi, Pixel, OnePlus и др.).
    /// </summary>
    public class QuantSniperEngine
    {
        // Матрица ликвидности брендов Android (вес от 0.7 до 1.0)
        private static readonly Dictionary<string, double> AndroidLiquidityWeights = new(StringComparer.OrdinalIgnoreCase)
        {
            { "samsung", 1.0 },
            { "galaxy", 1.0 },
            { "xiaomi", 0.95 },
            { "redmi", 0.95 },
            { "poco", 0.90 },
            { "google", 0.95 },
            { "pixel", 0.95 },
            { "oneplus", 0.85 },
            { "realme", 0.85 },
            { "honor", 0.80 },
            { "sony", 0.75 },
            { "motorola", 0.75 },
            { "asus", 0.70 },
            { "tecno", 0.70 },
            { "infinix", 0.70 }
        };

        private readonly AntiScamService? _antiScamService;

        public QuantSniperEngine(AntiScamService? antiScamService = null)
        {
            _antiScamService = antiScamService;
        }

        /// <summary>
        /// Вычисляет базовую справедливую цену (Fair Value) по исторической выборке Android моделей
        /// </summary>
        public decimal CalculateFairValue(List<Listing> historicalListings, string targetModel)
        {
            if (historicalListings == null || historicalListings.Count == 0)
                return 0m;

            var relevantPrices = historicalListings
                .Where(l => l.PriceValue > 0 && !string.IsNullOrWhiteSpace(l.Model) &&
                            l.Model.Contains(targetModel, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.PriceValue)
                .OrderBy(p => p)
                .ToList();

            if (relevantPrices.Count == 0)
            {
                // Fallback: медиана по всей выборке сходного диапазона
                var allSorted = historicalListings.Where(l => l.PriceValue > 0).Select(l => l.PriceValue).OrderBy(p => p).ToList();
                return allSorted.Count > 0 ? allSorted[allSorted.Count / 2] : 0m;
            }

            // Медиана выборки (устойчива к выбросам)
            int mid = relevantPrices.Count / 2;
            return relevantPrices.Count % 2 != 0 ? relevantPrices[mid] : (relevantPrices[mid - 1] + relevantPrices[mid]) / 2m;
        }

        /// <summary>
        /// Комплексная количественная оценка сделки
        /// </summary>
        public QuantEvaluationResult Evaluate(Listing listing, decimal fairValuePrice, bool hasPhotoDuplicate = false, bool isChronicDealer = false)
        {
            string modelName = !string.IsNullOrWhiteSpace(listing.Model) ? listing.Model : listing.Title;
            var result = new QuantEvaluationResult
            {
                ModelName = modelName,
                ActualPrice = listing.PriceValue,
                FairValuePrice = fairValuePrice
            };

            if (fairValuePrice <= 0m || listing.PriceValue <= 0m)
            {
                result.Rating = QuantRating.FairMarket;
                result.QuantScore = 50;
                result.BadgeText = "⚖️ РЫНОК";
                result.BadgeColor = "#6B7280";
                result.Factors.Add("Недостаточно данных для точного Fair Value");
                return result;
            }

            // 1. Расчёт дисконта и ожидаемой маржи
            decimal margin = fairValuePrice - listing.PriceValue;
            double discountPercent = (double)(margin / fairValuePrice) * 100.0;
            result.DiscountPercent = Math.Round(discountPercent, 1);
            result.EstimatedNetMargin = margin;

            // 2. Ликвидность Android бренда
            double brandWeight = GetBrandLiquidityWeight(modelName + " " + listing.Brand);
            result.Factors.Add($"Коэффициент ликвидности бренда: {brandWeight:F2}");

            // 3. Базовый скоринг на основе дисконта
            double baseScore = 50.0;
            if (discountPercent > 0)
            {
                baseScore += (discountPercent * 1.5) * brandWeight;
            }
            else
            {
                baseScore -= Math.Abs(discountPercent) * 1.2;
            }

            // 4. Проверка на риск и аномалии (Anti-Scam Guard)
            bool isAnomalousDiscount = discountPercent > 55.0; // Скидка > 55% без дефектов — признак скама
            if (hasPhotoDuplicate)
            {
                baseScore -= 60;
                result.IsHighRisk = true;
                result.Factors.Add("🔴 ОБНАРУЖЕН ДУБЛИКАТ ФОТО (Высокий риск мошенничества)");
            }

            if (isAnomalousDiscount)
            {
                baseScore -= 40;
                result.IsHighRisk = true;
                result.Factors.Add("⚠️ Аномально низкая цена (>55% ниже рынка)");
            }

            if (isChronicDealer)
            {
                baseScore -= 10;
                result.Factors.Add("ℹ️ Продавец-перекупщик (аккаунт в хроническом списке)");
            }

            int finalScore = Math.Clamp((int)Math.Round(baseScore), 0, 100);
            result.QuantScore = finalScore;

            // 5. Определение вердикта и визуального бейджа
            if (result.IsHighRisk || finalScore < 30)
            {
                result.Rating = QuantRating.ScamRisk;
                result.BadgeText = "🔴 СКАМ-РИСК";
                result.BadgeColor = "#EF4444";
            }
            else if (finalScore >= 80 && discountPercent >= 20.0)
            {
                result.Rating = QuantRating.SniperDeal;
                result.BadgeText = $"🔥 СНАЙПЕР (+{discountPercent:F0}%)";
                result.BadgeColor = "#10B981";
                result.Factors.Add("🟢 Идеальное соотношение маржи и безопасности");
            }
            else if (finalScore >= 65 && discountPercent >= 10.0)
            {
                result.Rating = QuantRating.GoodBuy;
                result.BadgeText = $"🟢 ВЫГОДНО (+{discountPercent:F0}%)";
                result.BadgeColor = "#06B6D4";
            }
            else if (finalScore >= 40)
            {
                result.Rating = QuantRating.FairMarket;
                result.BadgeText = "🟡 РЫНОК";
                result.BadgeColor = "#F59E0B";
            }
            else
            {
                result.Rating = QuantRating.Overpriced;
                result.BadgeText = "⚪ ДОРОГО";
                result.BadgeColor = "#9CA3AF";
            }

            return result;
        }

        private static double GetBrandLiquidityWeight(string brandOrModel)
        {
            if (string.IsNullOrWhiteSpace(brandOrModel))
                return 0.80;

            foreach (var kvp in AndroidLiquidityWeights)
            {
                if (brandOrModel.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return 0.80; // Базовый вес для других Android брендов
        }
    }
}
