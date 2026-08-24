using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class DataAnalysisService
    {
        private readonly ListingEvaluationService _evaluationService;
        private readonly Serilog.ILogger _logger = Serilog.Log.Logger;
        private readonly MetricsService _metricsService;
        private readonly ModelParserService _modelParserService;
        private readonly RetailPromoService _retailPromoService;

        // New extracted services
        private readonly ClassificationService _classificationService;
        private readonly ChronicSellerService _chronicSellerService;
        private readonly PriceAnalysisService _priceAnalysisService;
        private readonly MatchingClientService _matchingClient = new MatchingClientService();

        // Default constructor for legacy callers (uses new instances)
        public DataAnalysisService() : this(new MetricsService(new MetricsRepository())) { }

        public DataAnalysisService(MetricsService metricsService) : this(metricsService, new RetailPromoService()) { }

        public DataAnalysisService(MetricsService metricsService, RetailPromoService retailPromoService)
        {
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
            _retailPromoService = retailPromoService ?? throw new ArgumentNullException(nameof(retailPromoService));
            _evaluationService = new ListingEvaluationService();
            _modelParserService = new ModelParserService();

            // Initialize new extracted services
            _classificationService = new ClassificationService();
            _chronicSellerService = new ChronicSellerService();
            _priceAnalysisService = new PriceAnalysisService();
        }

        /// <summary>
        /// Full DI constructor — enables price trends via DB.
        /// </summary>
        public DataAnalysisService(MetricsService metricsService, RetailPromoService retailPromoService, DatabaseService databaseService)
        {
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
            _retailPromoService = retailPromoService ?? throw new ArgumentNullException(nameof(retailPromoService));
            _evaluationService = new ListingEvaluationService();
            _modelParserService = new ModelParserService();
            _classificationService = new ClassificationService();
            _chronicSellerService = new ChronicSellerService();
            _priceAnalysisService = new PriceAnalysisService(databaseService);
        }

        public AnalysisResult Analyze(List<Listing> listings, List<string> blacklist, List<string> blacklistLogins, Dictionary<string, decimal>? priceHistory = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Сброс статуса соединения для новой попытки подключения
            MatchingClientService.Reset();

            // Подсчет плотности предложений для каждой модели (спрос/предложение)
            var modelSupplyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in listings)
            {
                if (!string.IsNullOrEmpty(l.Model))
                {
                    if (!modelSupplyCounts.TryGetValue(l.Model, out var count)) count = 0;
                    modelSupplyCounts[l.Model] = count + 1;
                }
            }

            var analysisResult = new AnalysisResult
            {
                TotalListings = listings.Count,
                AnalysisDate = DateTime.Now
            };

            // Phase 1: Classify listings (commercial vs private, blacklist filtering)
            var classificationResult = _classificationService.Classify(listings, blacklist, blacklistLogins);
            var privateListings = classificationResult.PrivateListings;
            var commercialListings = classificationResult.CommercialListings;

            analysisResult.PrivateListings = privateListings.Count;
            analysisResult.CommercialListings = commercialListings.Count;
            analysisResult.FilteredByBlacklist = classificationResult.FilteredByBlacklist;

            // Phase 2: Detect chronic sellers
            analysisResult.ChronicSellers = _chronicSellerService.Detect(listings);

            // Phase 3: Price analysis (brand stats, model reference prices)
            var retailPromoMap = _retailPromoService.LoadPromoPriceMap();
            var priceAnalysisResult = _priceAnalysisService.Analyze(privateListings);

            analysisResult.MinPrice = priceAnalysisResult.MinPrice;
            analysisResult.MaxPrice = priceAnalysisResult.MaxPrice;
            analysisResult.AveragePrice = priceAnalysisResult.AveragePrice;
            analysisResult.MedianPrice = priceAnalysisResult.MedianPrice;
            analysisResult.BrandStats = priceAnalysisResult.BrandStats;
            analysisResult.ListingsConsidered = priceAnalysisResult.ListingsConsidered;
            analysisResult.PriceBuckets = priceAnalysisResult.PriceBuckets;
            analysisResult.BrandTrends = priceAnalysisResult.BrandTrends;

            // Phase 4: Extract models, battery health, defects
            foreach (var listing in privateListings)
            {
                listing.Model = _modelParserService.ExtractModel(listing.Title, listing.Brand, useSemantic: true);
                listing.BatteryHealth = ExtractBatteryHealth(listing.Title, listing.Description, listing.Brand);

                var defectResult = DetectDefectsAndEstimateRepair(listing.Title, listing.Description, listing.Brand, listing.Model, listing.BatteryHealth);
                listing.Defects = defectResult.defects;
                listing.RepairCost = defectResult.repairCost;
                listing.IsStolen = defectResult.isStolen;
                listing.HasCriticalDefect = defectResult.isCritical;
            }

            // Phase 5: Evaluate each listing (reference price, deviation, promo, hot deals)
            int promoMatches = 0;
            var promoMatchesByBrand = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var l in privateListings)
            {
                decimal referencePrice = 0m;
                string valueLabel = "";

                var exactKey = new ExactModelKey { Brand = l.Brand, Model = l.Model, StorageGB = l.StorageGB, IsNew = l.IsNew };
                var generalKey = new GeneralModelKey { Brand = l.Brand, Model = l.Model, IsNew = l.IsNew };

                string condLabel = l.IsNew ? "новых" : "б/у";

                if (l.StorageGB > 0 && priceAnalysisResult.ExactModelPrices.TryGetValue(exactKey, out var exactVal) && exactVal.Count >= 2)
                {
                    referencePrice = exactVal.MedianPrice;
                    valueLabel = $"{condLabel} моделей c " + (l.StorageGB == 1024 ? "1 TB" : l.StorageGB + " GB");
                }
                else if (priceAnalysisResult.GeneralModelPrices.TryGetValue(generalKey, out var generalVal) && generalVal.Count >= 2)
                {
                    referencePrice = generalVal.MedianPrice;
                    valueLabel = $"{condLabel} моделей";
                }
                else
                {
                    var baselineInfo = ModelPriceBaselineService.GetBaseline(l.Brand, l.Model, l.StorageGB);
                    if (baselineInfo != null)
                    {
                        referencePrice = baselineInfo.BaselinePrice;
                        valueLabel = $"базовых {condLabel} ({baselineInfo.ModelGroup})";
                    }
                    else
                    {
                        var brandStat = analysisResult.BrandStats.FirstOrDefault(b => b.Brand == l.Brand);
                        if (brandStat != null && brandStat.Count >= 3)
                        {
                            referencePrice = Math.Min(brandStat.MedianPrice, 2000m);
                            valueLabel = $"брендовых {condLabel}";
                        }
                        else
                        {
                            referencePrice = Math.Min(analysisResult.MedianPrice, 1800m);
                            valueLabel = $"общерыночных {condLabel}";
                        }
                    }
                }

                l.ModelAveragePrice = referencePrice;
                
                // Оценка дефицита модели на рынке (спрос/предложение)
                double scarcityMultiplier = 1.0;
                int supplyCount = 0;
                if (!string.IsNullOrEmpty(l.Model) && modelSupplyCounts.TryGetValue(l.Model, out supplyCount))
                {
                    if (supplyCount <= 3) // Дефицитная модель
                    {
                        scarcityMultiplier = 1.03; // Наценка +3%
                    }
                    else if (supplyCount >= 15) // Перенасыщенный рынок
                    {
                        scarcityMultiplier = 0.97; // Скидка -3% для быстрого сбыта
                    }
                }

                // Рекомендуемая цена продажи с учетом дефицита
                decimal baseResellPrice = referencePrice * (decimal)scarcityMultiplier;

                // Применяем Charm Pricing (психологическое округление)
                bool isPremium = baseResellPrice >= 15000m || (l.Brand != null && l.Brand.Equals("Apple", StringComparison.OrdinalIgnoreCase));
                l.RecommendedResellPrice = CharmPricingService.ApplyCharmPricing(baseResellPrice, isPremium);

                // Рассчитываем чистую маржу на основе RecommendedResellPrice
                l.NetProfitMargin = l.IsStolen ? 0m : (l.RecommendedResellPrice - (l.PriceValue + l.RepairCost));

                if (referencePrice > 0m)
                {
                    decimal priceDeviation = (referencePrice - l.PriceValue) / referencePrice * 100m;
                    l.ModelPriceDeviationPercent = Math.Round((double)priceDeviation, 1);

                    // metric: check if a promo exists for this listing
                    try
                    {
                        var promo = _evaluationService.GetBestPromo(l, retailPromoMap);
                        if (promo != null)
                        {
                            l.RetailPrice = promo.Price;
                            l.RetailShopName = promo.Shop;
                            l.RetailSavings = promo.Price - l.PriceValue;
                            l.RetailSavingsPercent = promo.Price > 0 ? (double)((promo.Price - l.PriceValue) / promo.Price * 100m) : 0.0;

                            promoMatches++;
                            if (!string.IsNullOrEmpty(l.Brand))
                            {
                                if (!promoMatchesByBrand.TryGetValue(l.Brand, out var bcount)) bcount = 0;
                                promoMatchesByBrand[l.Brand] = bcount + 1;
                                try
                                {
                                    var brandNorm = PromoKeyHelper.SanitizePrefix(l.Brand).TrimEnd('_');
                                    _metricsService.Increment($"promo_matches_brand_{brandNorm}", 1);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    double score = _evaluationService.EvaluateScore(l, priceDeviation, referencePrice, retailPromoMap, priceHistory);
                    _evaluationService.ApplyComparisonText(l, priceDeviation, valueLabel, retailPromoMap);

                    // Валидация цены через локальный шлюз безопасности (Python API)
                    var validationResult = _matchingClient.ValidatePriceAsync(
                        referencePrice,
                        referencePrice * 0.70m, // Условная себестоимость (30% ниже рынка)
                        referencePrice,
                        l.PriceValue
                    ).GetAwaiter().GetResult();

                    // Sanity Check: завышенные скидки (>40%) для старых бюджетных моделей требуют ручной проверки
                    var baselineCheck = ModelPriceBaselineService.GetBaseline(l.Brand, l.Model, l.StorageGB);
                    bool isLegacyBudgetModel = baselineCheck?.IsLegacyBudget ?? false;
                    bool requiresManualReview = priceDeviation >= 40m && (isLegacyBudgetModel || !priceAnalysisResult.ExactModelPrices.ContainsKey(exactKey));

                    if (ListingClassifier.IsBuyAd(l.Title, l.Description))
                    {
                        l.ComparisonText = "⛔ Скупка / Покупка";
                        l.ComparisonColor = "#8E8E93";
                        l.ComparisonBg = "#F2F2F7";
                        score = 0.0;
                    }
                    else if (ListingClassifier.IsFeatureOrRetroPhone(l.Title, l.Brand))
                    {
                        l.ComparisonText = "⛔ Кнопочный / Ретро";
                        l.ComparisonColor = "#8E8E93";
                        l.ComparisonBg = "#F2F2F7";
                        score = 0.0;
                    }
                    else if (ListingClassifier.IsAppleOrIPhone(l.Title, l.Brand))
                    {
                        l.ComparisonText = "⚠️ Исключено: iPhone";
                        l.ComparisonColor = "#8E8E93";
                        l.ComparisonBg = "#F2F2F7";
                        score = 0.0;
                    }
                    else if (requiresManualReview)
                    {
                        l.ComparisonText = "⚠️ Требует ручной проверки рыночной цены";
                        l.ComparisonColor = "#E65100"; // Оранжевый цвет предупреждения
                        l.ComparisonBg = "#FFF3E0";    // Бледный оранжевый фон
                        score = Math.Min(score, 45.0); // Ограничиваем балл, чтобы не было ложной "Горячей сделки"
                    }
                    else if (validationResult != null && !validationResult.Value.IsValid)
                    {
                        l.ComparisonText = "⚠️ " + validationResult.Value.WarningMessage;
                        l.ComparisonColor = "#FF3B30"; // Красный цвет ошибки
                        l.ComparisonBg = "#FFE5E5";    // Бледный красный фон
                        score = 0.0; // Аннулируем оценку, чтобы не попало в "Горячие сделки"
                    }

                    // Генерируем цепочку рассуждений ИИ-агента (Рассуждения Совета Комитетов)
                    var reasoning = new System.Text.StringBuilder();
                    reasoning.AppendLine("🏛️ ВЕРДИКТ СОВЕТА ИИ-КОМИТЕТОВ (ИИ-СУДЬЯ: СКТЕПТИК-ПЕРЕКУПЩИК):");
                    reasoning.AppendLine("────────────────────────────────────────");
                    reasoning.AppendLine("🧠 ИНСТРУКЦИЯ ИИ-СУДЬИ:");
                    reasoning.AppendLine("«Ты — циничный и опытный перекупщик смартфонов. По умолчанию каждое объявление — это обычная рядовая цена. Твоя задача — найти подвох, проверить возраст устройства и доказать, что выгоды нет. Если устройство старое (например, Redmi 9), цена в 1000 лей — это нормальный рынок, а не аномальная скидка. Не ставь высокий приоритет и статус 'Горячая сделка', если нет железобетонных доказательств реальной перекупщицкой маржи.»");
                    reasoning.AppendLine();

                    if (isLegacyBudgetModel)
                    {
                        reasoning.AppendLine("[ПРЕДУПРЕЖДЕНИЕ: Устаревшее бюджетное устройство. Цена в районе 1000 лей для него — это нормальный рынок, а не аномальная скидка]");
                        reasoning.AppendLine();
                    }

                    reasoning.AppendLine("🔍 КОМИТЕТ РАЗВЕДКИ И НОРМАЛИЗАЦИИ:");
                    reasoning.AppendLine($"• Модель: {l.Brand} {l.Model} ({(l.StorageGB > 0 ? l.StorageGB + " GB" : "N/A")}, {(l.IsNew ? "новый" : "б/у")})");
                    reasoning.AppendLine($"• Классификация источника: {l.CategoryBadgeText} ({l.SellerType})");

                    reasoning.AppendLine();
                    reasoning.AppendLine($"🏷️ СТРАТЕГИЯ ОЦЕНКИ ПО КАТЕГОРИИ ПРОДАВЦА ({l.CategoryBadgeText}):");
                    switch (l.NewSmartphoneCategory)
                    {
                        case "RetailChain":
                            reasoning.AppendLine("• Регламент ИИ: Крупный официальный ритейл. Оценивать честность промо-скидки относительно базовой цены. Учитывать 24 мес. гарантии.");
                            break;
                        case "Shop999":
                            reasoning.AppendLine($"• Регламент ИИ: Магазин 999.md / Импорт. Сравнивать цену с крупным ритейлом (~{referencePrice * 1.15m:F0} MDL). Зафиксировать маржу выгоды ({referencePrice * 1.15m - l.PriceValue:F0} MDL).");
                            break;
                        case "PrivateNew":
                            reasoning.AppendLine("• Регламент ИИ: Частник (Новый/Запечатанный). Проверять наличие упаковки/пломб, чека и дисконт относительно магазинов.");
                            break;
                        default:
                            reasoning.AppendLine("• Регламент ИИ: Б/у смартфон от частного продавца. Проверять износ, дефекты и реальную выгоду.");
                            break;
                    }

                    reasoning.AppendLine();
                    reasoning.AppendLine("💸 КОМИТЕТ ФИНАНСОВОЙ ОЦЕНКИ (ФАКТЫ):");
                    decimal priceRangeLow = Math.Round(referencePrice * 0.85m);
                    decimal priceRangeHigh = Math.Round(referencePrice * 1.15m);
                    reasoning.AppendLine($"• Цена продавца: {l.PriceValue:F0} MDL");
                    reasoning.AppendLine($"• Реальный ценовой диапазон модели на рынке: {priceRangeLow:F0}–{priceRangeHigh:F0} MDL (Ориентир медианы: {referencePrice:F0} MDL)");
                    reasoning.AppendLine($"• Абсолютная выгода к базовой медиане: {referencePrice - l.PriceValue:F0} MDL");
                    
                    if (scarcityMultiplier > 1.0)
                    {
                        reasoning.AppendLine($"• Спрос/Предложение: Дефицит модели ({supplyCount} объявлений). Наценка +3%.");
                    }
                    else if (scarcityMultiplier < 1.0)
                    {
                        reasoning.AppendLine($"• Спрос/Предложение: Избыток предложений ({supplyCount} объявлений). Скидка -3%.");
                    }
                    else
                    {
                        reasoning.AppendLine($"• Спрос/Предложение: Стабильный спрос ({supplyCount} объявлений).");
                    }
                    reasoning.AppendLine($"• Оценка: Рекомендация к перепродаже: {l.RecommendedResellPrice:F0} MDL (Чистая маржа: {l.NetProfitMargin:F0} MDL)");

                    reasoning.AppendLine();
                    reasoning.AppendLine("⚠️ КОМИТЕТ РИСКОВ И ДЕФЕКТОВ:");
                    reasoning.AppendLine($"• Здоровье батареи (АКБ): {(l.BatteryHealth > 0 ? l.BatteryHealth + "%" : "Не указано / Не применимо")}");
                    reasoning.AppendLine($"• Обнаружено дефектов: {(l.Defects.Count > 0 ? l.Defects.Count.ToString() : "Нет")}");
                    if (l.RepairCost > 0m)
                    {
                        reasoning.AppendLine($"• Стоимость ремонта: {l.RepairCost:F0} MDL");
                    }
                    reasoning.AppendLine($"• Сигнал кражи / Неоригинал: {(l.IsStolen ? "КРИТИЧЕСКИЙ (Найдены маркеры)" : "Чисто")}");
                    reasoning.AppendLine($"• Критический износ: {(l.HasCriticalDefect ? "Да (Экран бит / не включается)" : "Нет")}");

                    var osService = new XiaomiOsSupportService();
                    var osSupport = osService.AnalyzeModel(l.Title);
                    if (osSupport.IsXiaomiDevice)
                    {
                        reasoning.AppendLine();
                        reasoning.AppendLine("🤖 АНАЛИЗ ПОДДЕРЖКИ ОС:");
                        reasoning.AppendLine($"• {osSupport.OsStatusSummary}");
                    }

                    reasoning.AppendLine();
                    reasoning.AppendLine("⚖️ ПРЕЗИДИУМ ПРИНЯТИЯ РЕШЕНИЙ (АРБИТР):");
                    string verdict = "Рядовая цена (Нет сверхприбыли)";
                    string recommendation = "Обычная рыночная цена. Перепродажа малоэффективна.";
                    if (l.IsStolen)
                    {
                        verdict = "🚨 ЭКСТРЕМАЛЬНЫЙ РИСК (Возможно краденый / Заблокирован)";
                        recommendation = "Покупка категорически не рекомендуется.";
                    }
                    else if (requiresManualReview)
                    {
                        verdict = "⚠️ ТРЕБУЕТ РУЧНОЙ ПРОВЕРКИ РЫНОЧНОЙ ЦЕНЫ";
                        recommendation = "Модель старая/бюджетная или скидка аномальная. Обязательна ручная проверка перед покупкой.";
                    }
                    else if (l.HasCriticalDefect || (score <= 40.0 && l.Defects.Count > 0))
                    {
                        verdict = "🔴 БРАК / ОТКЛОНЕНО (Дефект железа / Дрова)";
                        recommendation = "Критический дефект (потекшая матрица/битый экран/дефекты). Ремонт съест всю выгоду. Не брать!";
                        score = Math.Min(score, 25.0);
                    }
                    else if (l.Defects.Count > 0)
                    {
                        verdict = "⚠️ СПОРНАЯ СДЕЛКА (Имеются дефекты)";
                        recommendation = "Покупать только под ремонт или на запчасти с учетом большого торга.";
                    }
                    else if (l.NetProfitMargin >= 400m && score >= 70.0 && !l.HasCriticalDefect && l.Defects.Count == 0)
                    {
                        verdict = "🔥 ОТЛИЧНОЕ ПРЕДЛОЖЕНИЕ (Высокий приоритет)";
                        recommendation = "Подтвержденная высокая маржа. Покупать немедленно.";
                    }
                    else if (l.NetProfitMargin >= 250m && score >= 60.0 && !l.HasCriticalDefect && l.Defects.Count == 0)
                    {
                        verdict = "👍 ВЫГОДНАЯ СДЕЛКА (Средний приоритет)";
                        recommendation = "Хороший вариант для перепродажи.";
                    }
                    reasoning.AppendLine($"• Резюме: {verdict}");
                    reasoning.AppendLine($"• Действие: {recommendation}");

                    l.AiReasoning = reasoning.ToString();

                    if (HotDealBuilder.IsHotDeal(l, score))
                    {
                        analysisResult.HotDeals.Add(HotDealBuilder.Create(l, score, referencePrice, Math.Round((double)priceDeviation, 1)));
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

            analysisResult.AllPrivateListings = privateListings;
            analysisResult.HotDeals = analysisResult.HotDeals.OrderByDescending(h => h.RecommendationScore).ToList();

            _logger.Information("Analysis complete: total={Total}, hotDeals={HotDeals}, promoMatches={PromoMatches}",
                analysisResult.TotalListings, analysisResult.HotDeals.Count, promoMatches);
            _metricsService.Increment("analysis_runs", 1);
            _metricsService.Increment("promo_matches_total", promoMatches);
            _metricsService.SetGauge("hotdeals_current", analysisResult.HotDeals.Count);
            _metricsService.ObserveHistogram("analysis_duration_seconds", analysisResult.AnalysisDuration.TotalSeconds);

            foreach (var kv in promoMatchesByBrand)
            {
                _logger.Information("Promo matches for brand {Brand}: {Count}", kv.Key, kv.Value);
            }

            stopwatch.Stop();
            analysisResult.AnalysisDuration = stopwatch.Elapsed;
            return analysisResult;
        }

        private static int ExtractBatteryHealth(string title, string description, string brand)
        {
            if (!brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
                return 0;

            string text = (title + " " + description).ToLowerInvariant();
            var matches = Regex.Matches(text, @"(?:акб|battery|батаре|health|аккум|состояни|износ|bateri|viata|procent)\D*?(\d{2,3})\s*%?");
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int val))
                {
                    bool isWear = m.Value.Contains("износ");
                    if (isWear && val > 0 && val < 50)
                        val = 100 - val;
                    if (val >= 50 && val <= 100)
                        return val;
                }
            }

            var percentMatches = Regex.Matches(text, @"\b(\d{2})%");
            foreach (Match m in percentMatches)
            {
                if (int.TryParse(m.Groups[1].Value, out int val) && val >= 50 && val <= 100)
                    return val;
            }
            return 0;
        }

        public static (List<string> defects, decimal repairCost, bool isStolen, bool isCritical) DetectDefectsAndEstimateRepair(
            string title, string description, string brand, string model, int batteryHealth)
        {
            var defects = new List<string>();
            decimal repairCost = 0m;
            bool isStolen = false;
            bool isCritical = false;

            string text = (title + " " + description).ToLowerInvariant();

            // 1. iCloud / MDM / Lock / Stolen checks
            if (Regex.IsMatch(text, @"\b(?:icloud|айклауд|cont|blocked|blocat|r-sim|r sim|rsim|gevey|mdm|bypass|байпас|заблокирован|блокировк|заблокир|activation lock|активац)\w*"))
            {
                defects.Add("Заблокирован (iCloud / MDM / R-SIM)");
                isStolen = true;
                isCritical = true;
            }

            // 2. Screen issues
            if (Regex.IsMatch(text, @"\b(?:разбит|трещин|скол|spart|sticl|crap|defect|пятн|pete|полос|lines|lovit|burn.*in|выгоран)\w*\b") &&
                Regex.IsMatch(text, @"(?:экран|дисплей|стекл|display|ecran|sticla|screen)\w*"))
            {
                defects.Add("Разбит/поврежден дисплей или стекло");
                repairCost += 2500m; // Generic screen replacement cost
                if (Regex.IsMatch(text, @"\b(?:на запчаст|на зп|части|parturi|piese)\w*\b"))
                    isCritical = true;
            }

            // 2.5 Screen replaced
            if (Regex.IsMatch(text, @"(?:меняный.*экран|экран.*менял|ecran schimbat|display schimbat|заменен.*экран|дисплей.*заменен|экран.*заменен|заменен.*дисплей)"))
            {
                defects.Add("Заменен дисплей (неоригинал)");
                repairCost += 1000m; // Penalize for non-original screen
            }

            // 3. FaceID / TouchID / Sensors
            if ((Regex.IsMatch(text, @"\b(?:faceid|face id|touchid|touch id|отпечат|сканер|распознаван)\w*") &&
                 Regex.IsMatch(text, @"\b(?:не.*работ|ошибк|defect|неактив|не актив|nu func|nu luc|off)\w*\b")) ||
                 Regex.IsMatch(text, @"\b(?:fara face|fără face|fara touch|fără touch|face id off)\w*\b"))
            {
                defects.Add("Не работает FaceID/TouchID");
                repairCost += 800m;
            }

            // 4. Critical power / charging issues
            if (Regex.IsMatch(text, @"\b(?:не заряжается|не заряж|заряд.*не|power.*issue|плохая зарядка|зарядка не)\w*\b"))
            {
                defects.Add("Проблемы с зарядкой");
                repairCost += 500m;
                if (Regex.IsMatch(text, @"\b(?:не включается|не заряжается|разблокировать|на запчаст)\w*\b"))
                    isCritical = true;
            }

            // 5. Battery Health
            if (Regex.IsMatch(text, @"\b(?:вздут|надул|не.*держит|мертв|dead|schimbat|менял|заменен|требует.*замен|service)\w*\b") &&
                Regex.IsMatch(text, @"(?:акб|battery|батаре|bateri)\w*"))
            {
                defects.Add("Требуется замена АКБ");
                repairCost += 800m;
            }
            else if (brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
            {
                if (batteryHealth > 0 && batteryHealth < 80)
                {
                    defects.Add($"Износ АКБ ({batteryHealth}%)");
                    repairCost += 400m;
                }
            }

            // 6. Back glass / cover damage
            if (Regex.IsMatch(text, @"\b(?:задн|крышк|back|spate)\w*\b") &&
                Regex.IsMatch(text, @"\b(?:разбит|трещин|spart|crap|defect)\w*\b"))
            {
                defects.Add("Разбита задняя крышка");
                repairCost += 1200m;
            }

            // 7. Camera issues
            if (Regex.IsMatch(text, @"(?:камер|camer)\w*") &&
                Regex.IsMatch(text, @"(?:мутн|тряс|не.*фокус|пятн|pete|defect|nu func|nu luc|разбит|spart)\w*"))
            {
                defects.Add("Дефект камеры");
                repairCost += 1800m;
            }

            // 8. Replica / non-original / fake devices
            if (Regex.IsMatch(text, @"\b(?:копия|реплика|fake|non[- ]?original|неоригинал|не оригинал|оригинал\?|non functional|clone)\w*\b"))
            {
                defects.Add("Непроверенный / копия");
                repairCost += 800m;
                isCritical = true;
            }

            // 9. Minor condition and wear
            if (Regex.IsMatch(text, @"\b(?:царап|вмят|скол|потертост|искривлен|люфт|сломанный)\w*\b") &&
                !Regex.IsMatch(text, @"\b(?:стекл.*защит|защитн.*стекл|pelicul|protector)\w*\b"))
            {
                defects.Add("Визуальные дефекты корпуса");
                repairCost += 300m;
            }

            if (Regex.IsMatch(text, @"\b(?:нерабоч|не.*работ|не работает|глюч|глюк|через раз|не включа|не зажига|не отвечает)\w*\b"))
            {
                defects.Add("Серьёзные проблемы в работе");
                repairCost += 800m;
                isCritical = true;
            }

            if (!isCritical && defects.Count == 0 && Regex.IsMatch(text, @"\b(на запчасти|запчасти|parts only|for parts)\b"))
            {
                defects.Add("На запчасти");
                repairCost += 1000m;
                isCritical = true;
            }

            return (defects, repairCost, isStolen, isCritical);
        }
    }
}