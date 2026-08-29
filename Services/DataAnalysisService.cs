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

            var authorListingCounts = privateListings
                .Where(l => !string.IsNullOrEmpty(l.AuthorLogin))
                .GroupBy(l => l.AuthorLogin, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var phoneListingCounts = privateListings
                .Where(l => !string.IsNullOrEmpty(l.PhoneNumber))
                .GroupBy(l => ListingClassifier.NormalizePhone(l.PhoneNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var l in privateListings)
            {
                decimal referencePrice = 0m;
                string valueLabel = "";
                int sampleCount = 0;

                var exactKey = new ExactModelKey { Brand = l.Brand, Model = l.Model, StorageGB = l.StorageGB, IsNew = l.IsNew };
                var generalKey = new GeneralModelKey { Brand = l.Brand, Model = l.Model, IsNew = l.IsNew };

                string condLabel = l.IsNew ? "новых" : "б/у";

                if (l.StorageGB > 0 && priceAnalysisResult.ExactModelPrices.TryGetValue(exactKey, out var exactVal) && exactVal.Count >= 2)
                {
                    referencePrice = exactVal.MedianPrice;
                    sampleCount = exactVal.Count;
                    valueLabel = $"{condLabel} моделей c " + (l.StorageGB == 1024 ? "1 TB" : l.StorageGB + " GB");
                }
                else if (priceAnalysisResult.GeneralModelPrices.TryGetValue(generalKey, out var generalVal) && generalVal.Count >= 2)
                {
                    referencePrice = generalVal.MedianPrice;
                    sampleCount = generalVal.Count;
                    valueLabel = $"{condLabel} моделей";
                }
                else
                {
                    var baselineInfo = ModelPriceBaselineService.GetBaseline(l.Brand, l.Model, l.StorageGB);
                    if (baselineInfo != null)
                    {
                        referencePrice = baselineInfo.BaselinePrice;
                        sampleCount = 0;
                        valueLabel = $"базовых {condLabel} ({baselineInfo.ModelGroup})";
                    }
                    else
                    {
                        var brandStat = analysisResult.BrandStats.FirstOrDefault(b => b.Brand == l.Brand);
                        if (brandStat != null && brandStat.Count >= 3)
                        {
                            referencePrice = Math.Min(brandStat.MedianPrice, 2000m);
                            sampleCount = brandStat.Count;
                            valueLabel = $"брендовых {condLabel}";
                        }
                        else
                        {
                            referencePrice = Math.Min(analysisResult.MedianPrice, 1800m);
                            sampleCount = 0;
                            valueLabel = $"общерыночных {condLabel}";
                        }
                    }
                }

                l.ModelAveragePrice = referencePrice;

                // 1. Детекция перекупщиков и витрин (Multi-listing / Showcase keywords)
                int authorListingCount = (!string.IsNullOrEmpty(l.AuthorLogin) && authorListingCounts.TryGetValue(l.AuthorLogin, out int ac)) ? ac : 1;
                string normPhone = ListingClassifier.NormalizePhone(l.PhoneNumber);
                int phoneListingCount = (!string.IsNullOrEmpty(normPhone) && phoneListingCounts.TryGetValue(normPhone, out int pc)) ? pc : 1;

                bool isReseller = authorListingCount >= 2 || phoneListingCount >= 2 ||
                                  ListingClassifier.IsResellerShowcase(l.Title, l.Description, l.AuthorLogin, Math.Max(authorListingCount, phoneListingCount)) ||
                                  l.SellerType.Equals("Shop", StringComparison.OrdinalIgnoreCase) ||
                                  l.SellerType.Equals("RESELLER", StringComparison.OrdinalIgnoreCase) ||
                                  l.IsCommercial;

                if (isReseller)
                {
                    l.SellerType = "RESELLER";
                    l.IsCommercial = true;
                    l.NewSmartphoneCategory = "Reseller";
                }

                // 2. Оценка возраста устройства и износа
                int ageYears = ModelPriceBaselineService.EstimateDeviceAgeYears(l.Brand, l.Model, l.Title);

                // 3. ЖЕСТКИЕ ПРАВИЛА ЦЕНООБРАЗОВАНИЯ ФИНАНСОВОГО КОМИТЕТА:
                // Запрещено ставить цену перепродажи выше медианы рынка.
                // Формула реалистичной быстрой перепродажи: 85% от медианы рынка.
                decimal baseResellPrice = referencePrice > 0m ? Math.Round(referencePrice * 0.85m) : 0m;

                bool isPremium = baseResellPrice >= 15000m || (l.Brand != null && l.Brand.Equals("Apple", StringComparison.OrdinalIgnoreCase));
                l.RecommendedResellPrice = CharmPricingService.ApplyCharmPricing(baseResellPrice, isPremium);

                // Формула чистой прибыли: Resale_Target - Listing_Price - Repair_Cost - 200 MDL (накладные расходы)
                decimal overheadCosts = 200m;
                l.NetProfitMargin = l.IsStolen ? 0m : (l.RecommendedResellPrice - l.PriceValue - l.RepairCost - overheadCosts);

                if (referencePrice > 0m)
                {
                    decimal priceDeviation = (referencePrice - l.PriceValue) / referencePrice * 100m;
                    l.ModelPriceDeviationPercent = Math.Round((double)priceDeviation, 1);

                    // Promo check
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

                    // 4. Антигаллюцинация выборки и спроса: если выборка < 3 объявлений — штраф 30%
                    bool isInsufficientData = sampleCount < 3;
                    if (isInsufficientData)
                    {
                        score *= 0.70;
                    }

                    // 5. Правило минимальной маржи: если чистая прибыль < 400 MDL -> сделка не может быть горячей (Score <= 60)
                    if (l.NetProfitMargin < 400m && score > 60.0)
                    {
                        score = 60.0;
                    }

                    // Sanity check for legacy models
                    var baselineCheck = ModelPriceBaselineService.GetBaseline(l.Brand, l.Model, l.StorageGB);
                    bool isLegacyBudgetModel = baselineCheck?.IsLegacyBudget ?? false || ageYears >= 4;
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
                    else if (isReseller)
                    {
                        l.ComparisonText = "🏬 Перекупщик / Витрина";
                        l.ComparisonColor = "#DC2626";
                        l.ComparisonBg = "#FEE2E2";
                        score = Math.Min(score, 35.0);
                    }
                    else if (requiresManualReview)
                    {
                        l.ComparisonText = "⚠️ Требует ручной проверки рыночной цены";
                        l.ComparisonColor = "#E65100";
                        l.ComparisonBg = "#FFF3E0";
                        score = Math.Min(score, 45.0);
                    }

                    // Генерируем цепочку рассуждений ИИ-агента (Рассуждения Совета Комитетов)
                    var reasoning = new System.Text.StringBuilder();
                    reasoning.AppendLine("🏛️ ВЕРДИКТ СОВЕТА ИИ-КОМИТЕТОВ (ИИ-СУДЬЯ: СКТЕПТИК-ПЕРЕКУПЩИК):");
                    reasoning.AppendLine("────────────────────────────────────────");
                    reasoning.AppendLine("🧠 ИНСТРУКЦИЯ ИИ-СУДЬИ:");
                    reasoning.AppendLine("«Ты — циничный и опытный перекупщик смартфонов. Твоя задача — найти подвох, проверить профиль продавца на признаки перекупщика/витрины, учесть реальный возраст устройства и запретить переоценку. Если устройство старше 3-5 лет или продается перекупщиком с витрины, покупка под перепродажу запрещена или строго ограничена.»");
                    reasoning.AppendLine();

                    reasoning.AppendLine("🔍 КОМИТЕТ РАЗВЕДКИ И ДЕТЕКЦИИ ПРОДАВЦА:");
                    reasoning.AppendLine($"• Модель: {l.Brand} {l.Model} ({(l.StorageGB > 0 ? l.StorageGB + " GB" : "N/A")}, {(l.IsNew ? "новый" : "б/у")})");
                    reasoning.AppendLine($"• Продавец: {l.AuthorLogin} | Объявлений в категории: {Math.Max(authorListingCount, phoneListingCount)}");
                    string sellerClassificationDesc = isReseller
                        ? "🚨 ВИТРИНА / ПЕРЕКУПЩИК"
                        : (l.SellerType == "FRESH_PRIVATE" || l.NewSmartphoneCategory == "FreshPrivate")
                            ? "🆕 СВЕЖИЙ ЧАСТНИК (Новый аккаунт, 1 лот в базе — максимальная вероятность реального владельца)"
                            : "Частный продавец";
                    reasoning.AppendLine($"• Классификация источника: {l.CategoryBadgeText} ({sellerClassificationDesc})");

                    reasoning.AppendLine();
                    reasoning.AppendLine("⏳ КОМИТЕТ ВОЗРАСТА И ИЗНОСА ОБОРУДОВАНИЯ:");
                    reasoning.AppendLine($"• Оценочный возраст модели: ~{ageYears} лет");
                    if (ageYears >= 5)
                    {
                        reasoning.AppendLine("• Штраф возраста: ⚠️ -25 баллов (Устаревшая платформа 5+ лет, риск деградации памяти/платы, отсутствие актуальной ОС). Score ограничен 65.");
                    }
                    else if (ageYears >= 3)
                    {
                        reasoning.AppendLine("• Штраф возраста: ⚠️ -15 баллов (Модель 3-4 года на рынке, износ компонентов).");
                    }
                    else
                    {
                        reasoning.AppendLine("• Статус платформы: Актуальная модель (до 2 лет).");
                    }

                    reasoning.AppendLine();
                    decimal totalCost = l.PriceValue + l.RepairCost + overheadCosts;
                    reasoning.AppendLine("💸 КОМИТЕТ ФИНАНСОВОЙ ОЦЕНКИ И АРБИТРАЖА:");
                    reasoning.AppendLine($"• Цена продавца: {l.PriceValue:F0} MDL");
                    if (l.RepairCost > 0m)
                    {
                        reasoning.AppendLine($"• Расходы на ремонт/запчасти: {l.RepairCost:F0} MDL");
                    }
                    reasoning.AppendLine($"• Накладные расходы (логистика, торг, риски): {overheadCosts:F0} MDL");
                    reasoning.AppendLine($"• Полная себестоимость проекта: {totalCost:F0} MDL");
                    reasoning.AppendLine($"• Медиана рынка: {referencePrice:F0} MDL (Размер выборки: {(sampleCount > 0 ? sampleCount.ToString() : "базовый каталог")})");
                    reasoning.AppendLine($"• Реалистичная цель быстрой перепродажи (85% от медианы): {l.RecommendedResellPrice:F0} MDL");
                    reasoning.AppendLine($"• Итоговая чистая маржа: {l.NetProfitMargin:F0} MDL {(l.NetProfitMargin < 400m ? "(⚠️ Меньше минимального порога 400 MDL — сделка не горячая)" : "(✅ Достаточная маржа >400 MDL)")}");

                    reasoning.AppendLine();
                    reasoning.AppendLine("📊 АНАЛИЗ ВЫБОРКИ И СПРОСА (АНТИГАЛЛЮЦИНАЦИЯ):");
                    if (isInsufficientData)
                    {
                        reasoning.AppendLine($"• Статус выборки: ⚠️ INSUFFICIENT_DATA (Выборка {sampleCount} объявлений < 3). Спрос не подтвержден. Оценка уверенности снижена на 30%.");
                    }
                    else
                    {
                        reasoning.AppendLine($"• Статус выборки: ✅ Достаточная выборка ({sampleCount} активных объявлений).");
                    }

                    reasoning.AppendLine();
                    reasoning.AppendLine("⚠️ КОМИТЕТ РИСКОВ И ДЕФЕКТОВ:");
                    reasoning.AppendLine($"• Здоровье батареи (АКБ): {(l.BatteryHealth > 0 ? l.BatteryHealth + "%" : "Не указано / Не применимо")}");
                    reasoning.AppendLine($"• Обнаружено дефектов: {(l.Defects.Count > 0 ? string.Join(", ", l.Defects) : "Нет")}");
                    if (l.RepairCost > 0m)
                    {
                        reasoning.AppendLine($"• Оценочная стоимость устранения: {l.RepairCost:F0} MDL");
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
                    else if (isReseller)
                    {
                        verdict = "⛔ ПЕРЕКУПЩИК / ВИТРИНА (Выгоды нет)";
                        recommendation = "Продавец — профессиональный перекупщик. Цена розничная, покупка для перепродажи принесет убыток.";
                    }
                    else if (ageYears >= 5 && l.PriceValue >= referencePrice * 0.80m)
                    {
                        verdict = "⏳ УСТАРЕВШЕЕ УСТРОЙСТВО (5+ лет)";
                        recommendation = "Старая платформа, EOL по обновлениям. Реальная ликвидность низкая.";
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
                    else if (l.Defects.Count > 0 && l.NetProfitMargin >= 400m && !l.HasCriticalDefect && !isReseller)
                    {
                        verdict = "🔧 ВЫГОДНАЯ СДЕЛКА ПОД РЕМОНТ (Маржа подтверждена)";
                        recommendation = $"Устранимый дефект ({string.Join(", ", l.Defects)}). С учетом стоимости запчастей ({l.RepairCost:F0} MDL) и накладных расходов чистая прибыль составляет {l.NetProfitMargin:F0} MDL. Выгодно под восстановление!";
                    }
                    else if (l.Defects.Count > 0)
                    {
                        verdict = "⚠️ СПОРНАЯ СДЕЛКА (Ремонт съедает маржу)";
                        recommendation = $"Имеются дефекты ({string.Join(", ", l.Defects)}). Стоимость ремонта ({l.RepairCost:F0} MDL) оставляет чистую прибыль {l.NetProfitMargin:F0} MDL (<400 MDL). Покупать только с большим торгом.";
                    }
                    else if (l.NetProfitMargin >= 400m && score >= 70.0 && !l.HasCriticalDefect && l.Defects.Count == 0 && !isReseller)
                    {
                        verdict = "🔥 ОТЛИЧНОЕ ПРЕДЛОЖЕНИЕ (Высокий приоритет)";
                        recommendation = "Подтвержденная высокая чистая маржа от частного владельца. Покупать немедленно.";
                    }
                    else if (l.NetProfitMargin >= 250m && score >= 60.0 && !l.HasCriticalDefect && l.Defects.Count == 0 && !isReseller)
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

            // 1. iCloud / MDM / Lock / Stolen checks (RO + RU + EN)
            if (Regex.IsMatch(text, @"\b(?:icloud|айклауд|cont|blocked|blocat|r-sim|r sim|rsim|gevey|mdm|bypass|байпас|заблокирован|блокировк|заблокир|activation lock|активац|blocat pe retea|blocat rețea)\w*"))
            {
                defects.Add("Заблокирован (iCloud / MDM / R-SIM / Rețea)");
                isStolen = true;
                isCritical = true;
            }

            // 2. Back glass / cover damage & replacement (RO + RU + EN)
            // «înlocuirea capacului», «capac spate spart», «capacul din spate», «schimb capac», «spatele spart», «capac crapat», «capac fisurat»
            if (Regex.IsMatch(text, @"(?:înlocuirea capacului|inlocuirea capacului|necesită.*înlocuirea.*capacului|necesita.*inlocuirea.*capacului|capac.*spate.*spart|capacul.*din.*spate|capac.*spate|spatele.*spart|schimb.*capac|schimbat.*capac|capac.*fisurat|capac.*crăpat|capac.*crapat|necesita.*schimb.*capac)") ||
                (Regex.IsMatch(text, @"\b(?:задн|крышк|back|spate)\w*\b") && Regex.IsMatch(text, @"\b(?:разбит|трещин|spart|crap|defect|замен|schimb|înlocuir|inlocuir)\w*\b")))
            {
                defects.Add("Разбита/повреждена задняя крышка (înlocuire capac)");
                // Для Samsung / Xiaomi / Android крышка 200-250 MDL + работа 100-150 MDL = 350 MDL; для Apple 600 MDL
                decimal backCoverCost = brand.Equals("Apple", StringComparison.OrdinalIgnoreCase) ? 600m : 350m;
                repairCost += backCoverCost;
            }

            // 3. Screen / Display / Glass issues (RO + RU + EN)
            // «ecran crăpat», «sticlă spartă», «pixeli morți», «display defect», «ecran negru», «linie pe ecran»
            if (Regex.IsMatch(text, @"(?:ecran.*crăpat|ecran.*crapat|sticlă.*spartă|sticla.*sparta|pixeli.*morți|pixeli.*morti|ecran.*lovit|sticlă.*fisurată|sticla.*fisurata|display.*defect|ecran.*negru|lini[ie].*ecran|ecran.*spart|display.*spart)") ||
                (Regex.IsMatch(text, @"\b(?:разбит|трещин|скол|spart|sticl|crap|defect|пятн|pete|полос|lines|lovit|burn.*in|выгоран)\w*\b") &&
                 Regex.IsMatch(text, @"(?:экран|дисплей|стекл|display|ecran|sticla|screen)\w*")))
            {
                defects.Add("Разбит/поврежден дисплей или стекло (ecran/sticlă)");
                decimal screenCost = brand.Equals("Apple", StringComparison.OrdinalIgnoreCase) ? 2500m : 1600m;
                repairCost += screenCost;
                if (Regex.IsMatch(text, @"\b(?:на запчаст|на зп|части|parturi|piese)\w*\b"))
                    isCritical = true;
            }

            // 3.5 Screen replaced (RO + RU)
            if (Regex.IsMatch(text, @"(?:меняный.*экран|экран.*менял|ecran schimbat|display schimbat|ecran inlocuit|ecran înlocuit|заменен.*экран|дисплей.*заменен|экран.*заменен|заменен.*дисплей)"))
            {
                defects.Add("Заменен дисплей (ecran schimbat)");
                repairCost += 800m;
            }

            // 4. FaceID / TouchID / Biometrics / Amprenta (RO + RU + EN)
            // «nu lucrează face id», «nu lucrează amprenta», «fără amprentă», «amprenta defectă», «face id nu merge»
            if (Regex.IsMatch(text, @"(?:nu.*lucreaz[aă].*face|nu.*lucreaz[aă].*amprent|f[aă]r[aă].*amprent|amprent[aă].*defect|face.*id.*nu.*merge|amprent[aă].*nu.*merge|nu.*cite[sș]te.*amprent|f[aă]r[aă].*face.*id)") ||
                ((Regex.IsMatch(text, @"\b(?:faceid|face id|touchid|touch id|отпечат|сканер|распознаван|amprent)\w*") &&
                  Regex.IsMatch(text, @"\b(?:не.*работ|ошибк|defect|неактив|не актив|nu func|nu luc|off|nu merge)\w*\b")) ||
                  Regex.IsMatch(text, @"\b(?:fara face|fără face|fara touch|fără touch|face id off)\w*\b")))
            {
                defects.Add("Не работает FaceID/TouchID/Amprenta");
                repairCost += 700m;
            }

            // 5. Battery Health / АКБ (RO + RU + EN)
            // «bateria ține puțin», «necesită schimb baterie», «baterie slabă», «baterie descărcată», «baterie degradată», «schimb acumulator»
            if (Regex.IsMatch(text, @"(?:bateria.*[tț]ine.*pu[tț]in|necesit[aă].*schimb.*bateri|baterie.*slab[aă]|baterie.*desc[aă]rcat[aă]|baterie.*uzat[aă]|baterie.*degradat[aă]|schimb.*acumulator|baterie.*moart[aă])") ||
                (Regex.IsMatch(text, @"\b(?:вздут|надул|не.*держит|мертв|dead|schimbat|менял|заменен|требует.*замен|service|ține puțin|tine putin)\w*\b") &&
                 Regex.IsMatch(text, @"(?:акб|battery|батаре|bateri|acumulator)\w*")))
            {
                defects.Add("Требуется замена АКБ (schimb baterie)");
                repairCost += 400m;
            }
            else if (brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
            {
                if (batteryHealth > 0 && batteryHealth < 80)
                {
                    defects.Add($"Износ АКБ ({batteryHealth}%)");
                    repairCost += 400m;
                }
            }

            // 6. Camera issues (RO + RU + EN)
            if (Regex.IsMatch(text, @"(?:camera.*defect[aă]|sticl[aă].*la.*camer|nu.*focalizeaz[aă]|camera.*nu.*lucreaz[aă]|camera.*tremur[aă])") ||
                (Regex.IsMatch(text, @"(?:камер|camer)\w*") &&
                 Regex.IsMatch(text, @"(?:мутн|тряс|не.*фокус|пятн|pete|defect|nu func|nu luc|разбит|spart)\w*")))
            {
                defects.Add("Дефект камеры (defect cameră)");
                repairCost += brand.Equals("Apple", StringComparison.OrdinalIgnoreCase) ? 1500m : 800m;
            }

            // 7. Charging port / Power issues (RO + RU + EN)
            if (Regex.IsMatch(text, @"(?:nu.*se.*[iî]ncarc[aă]|muf[aă].*defect[aă]|probleme.*[iî]nc[aă]rcare|nu.*[tț]ine.*[iî]nc[aă]rcarea)") ||
                Regex.IsMatch(text, @"\b(?:не заряжается|не заряж|заряд.*не|power.*issue|плохая зарядка|зарядка не|mufa)\w*\b"))
            {
                defects.Add("Проблемы с зарядкой / разъемом (port încărcare)");
                repairCost += 350m;
                if (Regex.IsMatch(text, @"\b(?:не включается|не зажига|на запчаст)\w*\b"))
                    isCritical = true;
            }

            // 8. Replica / non-original / fake devices
            if (Regex.IsMatch(text, @"\b(?:копия|реплика|fake|non[- ]?original|неоригинал|не оригинал|оригинал\?|non functional|clone)\w*\b"))
            {
                defects.Add("Непроверенный / копия (clone)");
                repairCost += 800m;
                isCritical = true;
            }

            // 9. Minor condition and wear
            if (Regex.IsMatch(text, @"\b(?:царап|вмят|скол|потертост|искривлен|люфт|sloit|zgariet|zgâriet)\w*\b") &&
                !Regex.IsMatch(text, @"\b(?:стекл.*защит|защитн.*стекл|pelicul|protector)\w*\b"))
            {
                defects.Add("Визуальные дефекты корпуса (zgârieturi)");
                repairCost += 200m;
            }

            if (Regex.IsMatch(text, @"\b(?:нерабоч|не.*работ|не работает|глюч|глюк|через раз|не включа|не зажига|не отвечает)\w*\b"))
            {
                defects.Add("Серьёзные проблемы в работе");
                repairCost += 800m;
                isCritical = true;
            }

            if (!isCritical && defects.Count == 0 && Regex.IsMatch(text, @"\b(на запчасти|запчасти|parts only|for parts|piese|la piese|pentru piese)\b"))
            {
                defects.Add("На запчасти (la piese)");
                repairCost += 1000m;
                isCritical = true;
            }

            return (defects, repairCost, isStolen, isCritical);
        }
    }
}