using System;

namespace SmartphoneMonitor.Services
{
    public static class CharmPricingService
    {
        /// <summary>
        /// Применяет психологические правила округления (Charm Pricing).
        /// </summary>
        public static decimal ApplyCharmPricing(decimal rawPrice, bool isPremium)
        {
            if (rawPrice <= 0m) return rawPrice;

            if (isPremium || rawPrice >= 15000m)
            {
                // Премиальный сегмент: Округляем до ближайших 500 или 1000 MDL (престижные ровные числа)
                decimal factor = 500m;
                if (rawPrice >= 20000m)
                {
                    factor = 1000m;
                }
                return Math.Round(rawPrice / factor) * factor;
            }
            else
            {
                // Бюджетный/Средний сегмент: Округляем до окончаний на 90 или 99 MDL (эффект скидки)
                decimal roundedToHundred = Math.Round(rawPrice / 100m) * 100m;
                if (rawPrice < roundedToHundred)
                {
                    return roundedToHundred - 10m; // например, 3800 -> 3790 MDL
                }
                else
                {
                    return roundedToHundred + 90m; // например, 3800 -> 3890 MDL
                }
            }
        }
    }
}
