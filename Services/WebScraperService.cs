using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartphoneMonitor.Models;
using Microsoft.Extensions.DependencyInjection;

namespace SmartphoneMonitor.Services
{
    public class WebScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly Random _random = new Random();

        private static readonly string[] _userAgents = new string[8]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_4_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15"
        };

        private static readonly Dictionary<string, int> _monthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ian", 1 }, { "ian.", 1 }, { "янв", 1 }, { "янв.", 1 }, { "jan", 1 }, { "jan.", 1 },
            { "feb", 2 }, { "feb.", 2 }, { "фев", 2 }, { "фев.", 2 },
            { "mar", 3 }, { "mar.", 3 }, { "мар", 3 }, { "мар.", 3 },
            { "apr", 4 }, { "apr.", 4 }, { "апр", 4 }, { "апр.", 4 },
            { "mai", 5 }, { "май", 5 }, { "may", 5 },
            { "iun", 6 }, { "iun.", 6 }, { "июн", 6 }, { "июн.", 6 }, { "jun", 6 }, { "jun.", 6 },
            { "iul", 7 }, { "iul.", 7 }, { "июл", 7 }, { "июл.", 7 }, { "jul", 7 }, { "jul.", 7 },
            { "aug", 8 }, { "aug.", 8 }, { "авг", 8 }, { "авг.", 8 },
            { "sep", 9 }, { "sep.", 9 }, { "sept", 9 }, { "sept.", 9 }, { "сен", 9 }, { "сен.", 9 }, { "сент", 9 }, { "сент.", 9 },
            { "oct", 10 }, { "oct.", 10 }, { "окт", 10 }, { "окт.", 10 },
            { "nov", 11 }, { "nov.", 11 }, { "ноя", 11 }, { "ноя.", 11 },
            { "dec", 12 }, { "dec.", 12 }, { "дек", 12 }, { "дек.", 12 }
        };

        public WebScraperService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("scraper");
        }

        // Legacy constructor for backward compatibility (creates HttpClient directly)
        [Obsolete("Use WebScraperService(IHttpClientFactory) instead to avoid socket exhaustion")]
        public WebScraperService() : this(CreateDefaultHttpClientFactory())
        {
        }

        private static IHttpClientFactory CreateDefaultHttpClientFactory()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddHttpClient("scraper", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        }

        public async Task<List<Listing>> ScrapeSmartphonesAsync(int maxPages, decimal eurToMdlRate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var listings = new List<Listing>();
            var seenUrls = new HashSet<string>();
            var seenContentKeys = new HashSet<string>();
            int consecutiveEmpty = 0;

            for (int page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Загрузка страницы {page}" + (maxPages >= 999 ? " (режим «Все»)" : $" из {maxPages}") + "...");
                try
                {
                    List<Listing> list = await FetchListingsGraphQLAsync(page, eurToMdlRate, cancellationToken);
                    int addedInPage = 0;
                    foreach (var item in list)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (seenUrls.Contains(item.Url))
                        {
                            continue;
                        }
                        
                        string contentKey = $"{item.Brand}_{item.Title.ToLowerInvariant()}_{item.PriceValue}_{item.StorageGB}_{item.SellerType}";
                        if (seenContentKeys.Contains(contentKey))
                        {
                            continue;
                        }

                        seenUrls.Add(item.Url);
                        seenContentKeys.Add(contentKey);
                        listings.Add(item);
                        addedInPage++;
                    }

                    progress?.Report($"Страница {page}: +{addedInPage} объявлений (итого {listings.Count}, отсеяно дубликатов: {list.Count - addedInPage})");
                    
                    if (list.Count == 0)
                    {
                        consecutiveEmpty++;
                        if (consecutiveEmpty >= 2)
                        {
                            progress?.Report("📄 Объявлений нет на 2 страницах подряд — конец каталога.");
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmpty = 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress?.Report($"⚠️ Ошибка загрузки страницы {page}: {ex.Message}");
                }

                await Task.Delay(page % 5 == 0 ? _random.Next(3000, 5000) : _random.Next(1500, 2500), cancellationToken);
            }
            return listings;
        }

        public async Task<(string phone, string seller, int views, string description, List<string> images)> FetchPhoneAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                int views = 0;
                string adId = ExtractAdIdFromUrl(url);
                if (!string.IsNullOrEmpty(adId))
                {
                    views = await FetchAdViewsGraphQLAsync(adId, cancellationToken);
                }

                var result = await FetchPageWithRetryAsync(url, "https://999.md/ro/list/phone-and-communication/mobile-phones", 2, cancellationToken);
                if (string.IsNullOrEmpty(result.html) || IsBlockedResponse(result.html, result.statusCode))
                {
                    return (phone: string.Empty, seller: string.Empty, views: views, description: string.Empty, images: new List<string>());
                }

                string html = result.html;
                string text3 = string.Empty;
                var match = Regex.Match(html, "\\\\\\\"phone_numbers\\\\\\\"\\s*:\\s*\\[\\s*\\\\\\\"([^\\\\\\\\]+)\\\\\\\"");
                if (match.Success)
                {
                    text3 = match.Groups[1].Value;
                }
                else
                {
                    var match2 = Regex.Match(html, "\\b(?:373|0)?[67]\\d{7}\\b");
                    if (match2.Success)
                    {
                        text3 = match2.Value;
                    }
                }

                if (!string.IsNullOrEmpty(text3))
                {
                    text3 = ListingClassifier.NormalizePhone(text3);
                    if (!string.IsNullOrEmpty(text3))
                    {
                        text3 = "+373" + text3;
                    }
                }

                string ownerLogin = string.Empty;
                var match3 = Regex.Match(html, "\\\\\\\"owner\\\\\\\"\\s*:\\s*\\{[^{}]*?\\\\\\\"login\\\\\\\"\\s*:\\s*\\\\\\\"([^\\\\\\\\]+)\\\\\\\"");
                if (match3.Success)
                {
                    ownerLogin = match3.Groups[1].Value;
                }

                string description = string.Empty;
                var descMatch = Regex.Match(html, @"\\?""description\\?""\s*:\s*\\?""((?:[^""\\]|\\.)*?)\\?""");
                if (descMatch.Success)
                {
                    string rawDesc = descMatch.Groups[1].Value;
                    try
                    {
                        description = JsonConvert.DeserializeObject<string>("\"" + rawDesc + "\"") ?? string.Empty;
                    }
                    catch
                    {
                        description = Regex.Unescape(rawDesc);
                    }
                }

                var images = new List<string>();
                var imageMatches = Regex.Matches(html, @"https?://i\.simpalsmedia\.com/999\.md/[^""'\s>\\]+");
                foreach (Match m in imageMatches)
                {
                    string imgUrl = m.Value.Replace("/320x240/", "/900x900/").Replace("900x900//", "900x900/");
                    if (!images.Contains(imgUrl))
                    {
                        images.Add(imgUrl);
                    }
                }

                return (phone: text3, seller: ownerLogin, views: views, description: description, images: images);
            }
            catch
            {
                return (phone: string.Empty, seller: string.Empty, views: 0, description: string.Empty, images: new List<string>());
            }
        }

        public async Task<List<string>> FetchListingImagesAsync(string url, CancellationToken cancellationToken = default)
        {
            var images = new List<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                System.Diagnostics.Debug.WriteLine("[Parser] Ошибка: URL объявления пуст!");
                return images;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://999.md" + (url.StartsWith("/") ? "" : "/") + url;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] Запрос детальной страницы для извлечения фото: {url}");
                var result = await FetchPageWithRetryAsync(url, "https://999.md/ro/list/phone-and-communication/mobile-phones", 3, cancellationToken);
                if (string.IsNullOrEmpty(result.html))
                {
                    System.Diagnostics.Debug.WriteLine($"[Parser] Не удалось получить HTML для {url} (StatusCode: {result.statusCode})");
                    return images;
                }

                string html = result.html;

                // 1. Direct simpalsmedia URLs matching
                var directMatches = Regex.Matches(html, @"https?://i\.simpalsmedia\.com/999\.md/[^""'\s>\\]+");
                foreach (Match m in directMatches)
                {
                    string imgUrl = m.Value.Replace("/320x240/", "/900x900/").Replace("900x900//", "900x900/");
                    if (imgUrl.EndsWith("\"") || imgUrl.EndsWith("'")) imgUrl = imgUrl.Substring(0, imgUrl.Length - 1);
                    if (!images.Contains(imgUrl))
                    {
                        images.Add(imgUrl);
                    }
                }

                // 2. HTML <img> tags, data-src, data-original, data-lazy, background-image attributes
                var attrMatches = Regex.Matches(html, @"(?:src|data-src|data-original|data-lazy|background(?:-image)?:\s*url\()\s*=\s*[""']?([^""'\s\)\\]+\.(?:jpg|jpeg|png|webp))", RegexOptions.IgnoreCase);
                foreach (Match m in attrMatches)
                {
                    string raw = m.Groups[1].Value;
                    if (raw.StartsWith("//")) raw = "https:" + raw;
                    else if (raw.StartsWith("/")) raw = "https://999.md" + raw;

                    if (raw.Contains("simpalsmedia") || raw.Contains("BoardImages"))
                    {
                        string imgUrl = raw.Replace("/320x240/", "/900x900/").Replace("900x900//", "900x900/");
                        if (!images.Contains(imgUrl))
                        {
                            images.Add(imgUrl);
                        }
                    }
                }

                // 3. Extract JSON image arrays or hash filenames in scripts
                var fileMatches = Regex.Matches(html, @"[a-f0-9]{32}\.(?:jpg|jpeg|png|webp)", RegexOptions.IgnoreCase);
                foreach (Match f in fileMatches)
                {
                    string fullUrl = $"https://i.simpalsmedia.com/999.md/BoardImages/900x900/{f.Value}";
                    if (!images.Contains(fullUrl))
                    {
                        images.Add(fullUrl);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Parser] Итого найдено фото для {url}: {images.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] Исключение при парсинге {url}: {ex.Message}");
            }

            return images;
        }

        private async Task<List<Listing>> FetchListingsGraphQLAsync(int page, decimal eurToMdlRate, CancellationToken cancellationToken)
        {
            var listings = new List<Listing>();
            string json = $"{{\n  \"input\": {{\n    \"source\": \"AD_SOURCE_DESKTOP_REDESIGN\",\n    \"sort\": \"SORT_ADS_DATE_DESC\",\n    \"pagination\": {{\n      \"limit\": 78,\n      \"skip\": {(page - 1) * 78}\n    }},\n    \"filters\": [\n      {{\n        \"filterId\": 6,\n        \"features\": [\n          {{\n            \"featureId\": 2,\n            \"unit\": \"UNIT_MDL\",\n            \"range\": {{\n              \"min\": \"1000\",\n              \"max\": \"5000\"\n            }}\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 16,\n        \"features\": [\n          {{\n            \"featureId\": 1,\n            \"optionIds\": [776]\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 1084,\n        \"features\": [\n          {{\n            \"featureId\": 593,\n            \"optionIds\": [6370, 6371]\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 290,\n        \"features\": [\n          {{\n            \"featureId\": 7,\n            \"optionIds\": [12900]\n          }}\n        ]\n      }}\n    ],\n    \"subCategoryId\": 40\n  }}\n}}";
            var value = new JObject
            {
                ["operationName"] = "SearchAds",
                ["variables"] = JObject.Parse(json),
                ["query"] = "query SearchAds($input: Ads_SearchInput!, $locale: Common_Locale) {\n  searchAds(input: $input) {\n    ads {\n      id\n      title\n      price: feature(id: 2) {\n        id\n        type\n        value\n      }\n      images: feature(id: 14) {\n        id\n        type\n        value\n      }\n      author: feature(id: 795) {\n        id\n        type\n        value\n      }\n      condition: feature(id: 593) {\n        id\n        type\n        value\n      }\n      brandFeature: feature(id: 589) {\n        id\n        type\n        value\n      }\n      storageFeature: feature(id: 1265) {\n        id\n        type\n        value\n      }\n      owner {\n        id\n        login\n        avatar\n        business {\n          plan\n        }\n      }\n      reseted(\n        input: {format: \"2 Jan. 2006, 15:04\", locale: $locale, timezone: \"Europe/Chisinau\", getDiff: false}\n      )\n      __typename\n    }\n    count\n    __typename\n  }\n}"
            };

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://999.md/graphql");
            httpRequestMessage.Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
            httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", _userAgents[_random.Next(_userAgents.Length)]);
            httpRequestMessage.Headers.TryAddWithoutValidation("Accept", "*/*");
            httpRequestMessage.Headers.TryAddWithoutValidation("Accept-Language", "ro-RO,ro;q=0.9,ru;q=0.8,en-US;q=0.7,en;q=0.6");
            httpRequestMessage.Headers.TryAddWithoutValidation("Referer", "https://999.md/ro/list/phone-and-communication/mobile-phones");
            httpRequestMessage.Headers.TryAddWithoutValidation("Origin", "https://999.md");

            var obj = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
            obj.EnsureSuccessStatusCode();
            var jObject = JsonConvert.DeserializeObject<JObject>(await obj.Content.ReadAsStringAsync());
            if (jObject == null)
            {
                return listings;
            }

            var jToken = jObject["data"];
            if (jToken == null)
            {
                return listings;
            }

            var jToken2 = jToken["searchAds"];
            if (jToken2 == null)
            {
                return listings;
            }

            var jToken3 = jToken2["ads"];
            if (jToken3 == null)
            {
                return listings;
            }

            foreach (var item in (IEnumerable<JToken>)jToken3)
            {
                try
                {
                    string id = ((string?)item["id"]) ?? "";
                    string title = ((string?)item["title"]) ?? "";
                    if (ListingClassifier.IsAccessoryOrPartOrOther(title) || ListingClassifier.IsFeatureOrRetroPhone(title) || ListingClassifier.IsAppleOrIPhone(title))
                    {
                        continue;
                    }

                    string conditionText = "б/у";
                    bool isNew = false;
                    try
                    {
                        var conditionVal = item["condition"]?["value"];
                        if (conditionVal != null)
                        {
                            string condStr = conditionVal.ToString();
                            if (condStr.Contains("6370"))
                            {
                                conditionText = "новый";
                                isNew = true;
                            }
                        }
                    }
                    catch { }

                    title = $"[{conditionText.ToUpperInvariant()}] {title}";
                    decimal price = default(decimal);
                    string unit = "";
                    var jToken4 = item["price"]?["value"];
                    if (jToken4 != null && jToken4["value"] != null)
                    {
                        price = (decimal?)jToken4["value"] ?? 0m;
                        unit = ((string?)jToken4["unit"]) ?? "";
                    }

                    if (unit == "UNIT_EUR")
                    {
                        price = Math.Round(price * eurToMdlRate, 0);
                    }

                    if (price > 0m && (price < 400m || price > 5000m))
                    {
                        continue;
                    }

                    string brand = "";
                    try
                    {
                        var brandVal = item["brandFeature"]?["value"]?["translated"];
                        if (brandVal != null)
                        {
                            brand = brandVal.ToString().Trim();
                        }
                    }
                    catch { }
                    if (string.IsNullOrEmpty(brand))
                    {
                        brand = ListingClassifier.DetectBrand(title);
                    }

                    int storageGB = 0;
                    try
                    {
                        var storageVal = item["storageFeature"]?["value"]?["translated"];
                        if (storageVal != null)
                        {
                            string storageStr = storageVal.ToString().ToLowerInvariant();
                            if (storageStr.Contains("tb") || storageStr.Contains("тб"))
                            {
                                storageGB = 1024;
                            }
                            else
                            {
                                var match = Regex.Match(storageStr, @"\d+");
                                if (match.Success)
                                {
                                    int.TryParse(match.Value, out storageGB);
                                }
                            }
                        }
                    }
                    catch { }
                    if (storageGB <= 0)
                    {
                        storageGB = ListingClassifier.ExtractStorage(title);
                    }

                    string authorTranslated = "";
                    var jToken5 = item["author"]?["value"];
                    if (jToken5 != null && jToken5["translated"] != null)
                    {
                        authorTranslated = ((string?)jToken5["translated"]) ?? "";
                    }

                    bool isShop = authorTranslated == "Magazin" || authorTranslated == "Companie" || authorTranslated == "Shop";
                    string reseted = ((string?)item["reseted"]) ?? "";
                    var postedDate = DateTime.Now;
                    int daysOld = -1;
                    if (!string.IsNullOrEmpty(reseted))
                    {
                        daysOld = ParseDaysOld(reseted);
                        if (daysOld >= 0)
                        {
                            postedDate = DateTime.Now.AddDays(-daysOld);
                        }
                    }

                    string ownerLogin = ((string?)item["owner"]?["login"]) ?? string.Empty;
                    bool isCommercial = ListingClassifier.IsCommercial(title, isShop ? "Shop" : "Private", ownerLogin, string.Empty, Array.Empty<string>(), Array.Empty<string>());
                    bool isUrgent = ListingClassifier.IsUrgent(title + " " + (isShop ? "magazin shop" : ""));

                    var imageUrls = new List<string>();
                    try
                    {
                        var imgsToken = item["images"]?["value"];
                        if (imgsToken != null)
                        {
                            foreach (var imgFile in imgsToken)
                            {
                                string fileName = imgFile.ToString().Trim();
                                if (!string.IsNullOrEmpty(fileName))
                                {
                                    string fullUrl = $"https://i.simpalsmedia.com/999.md/BoardImages/900x900/{fileName}";
                                    if (!imageUrls.Contains(fullUrl))
                                    {
                                        imageUrls.Add(fullUrl);
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    bool isBuyAd = ListingClassifier.IsBuyAd(item["title"]?.ToString() ?? "");
                    if (isBuyAd)
                    {
                        var demandItem = new DemandListing
                        {
                            Id = id,
                            Title = title,
                            BudgetPrice = price,
                            Description = title,
                            Url = "https://999.md/ro/" + id,
                            DateAdded = postedDate.ToString("o"),
                            AuthorLogin = ownerLogin,
                            Brand = brand,
                            Model = title,
                            StorageGB = storageGB,
                            IsProcessed = false
                        };
                        try
                        {
                            new DatabaseService().SaveDemandListings(new[] { demandItem });
                        }
                        catch { }
                        continue;
                    }

                    string newCat = ListingClassifier.DetermineNewSmartphoneCategory(authorTranslated, isShop ? "Shop" : "Private", isCommercial, isNew, "https://999.md/ro/" + id);

                    listings.Add(new Listing
                    {
                        Title = title,
                        Brand = brand,
                        PriceValue = price,
                        PriceDisplay = (price > 0m) ? $"{price:F0} lei" : "",
                        StorageGB = storageGB,
                        DaysOld = daysOld,
                        Views = 0,
                        Url = "https://999.md/ro/" + id,
                        SellerType = isShop ? "Shop" : "Private",
                        IsCommercial = isCommercial,
                        IsUrgent = isUrgent,
                        PostedDate = postedDate,
                        AuthorLogin = ownerLogin,
                        IsNew = isNew,
                        NewSmartphoneCategory = newCat,
                        ImageUrls = imageUrls
                    });
                }
                catch
                {
                }
            }
            return listings;
        }

        private static string ExtractAdIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }
            var match = Regex.Match(url, "\\b\\d{6,10}\\b");
            return match.Success ? match.Value : string.Empty;
        }

        private async Task<int> FetchAdViewsGraphQLAsync(string adId, CancellationToken cancellationToken)
        {
            try
            {
                var value = new JObject
                {
                    ["operationName"] = "AdViews",
                    ["variables"] = new JObject { ["input"] = new JObject { ["adId"] = adId } },
                    ["query"] = "query AdViews($input: Views_AdViewsRequestInput!) {\n  adViews(input: $input) {\n    total\n    __typename\n  }\n}"
                };
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://999.md/graphql");
                httpRequestMessage.Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
                httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", _userAgents[_random.Next(_userAgents.Length)]);
                
                var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    var jToken = JsonConvert.DeserializeObject<JObject>(await httpResponseMessage.Content.ReadAsStringAsync())?["data"]?["adViews"]?["total"];
                    if (jToken != null)
                    {
                        return (int)jToken;
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        private static int ParseDaysOld(string resetedStr)
        {
            if (string.IsNullOrWhiteSpace(resetedStr))
            {
                return -1;
            }
            try
            {
                var match = Regex.Match(resetedStr.Trim(), "^(\\d{1,2})\\s+([a-zA-Zа-яА-Я.]+)\\s+(\\d{4}),\\s+(\\d{1,2}):(\\d{2})");
                if (match.Success)
                {
                    int day = int.Parse(match.Groups[1].Value);
                    string monthStr = match.Groups[2].Value;
                    int year = int.Parse(match.Groups[3].Value);
                    int hour = int.Parse(match.Groups[4].Value);
                    int minute = int.Parse(match.Groups[5].Value);
                    if (_monthMap.TryGetValue(monthStr, out int monthVal))
                    {
                        // Use Europe/Chisinau timezone (UTC+3 / UTC+2 depending on DST)
                        var dateTime = new DateTime(year, monthVal, day, hour, minute, 0, DateTimeKind.Unspecified);
                        try
                        {
                            var chisinauTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Chisinau");
                            var utcTime = TimeZoneInfo.ConvertTimeToUtc(dateTime, chisinauTz);
                            return Math.Max(0, (int)(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chisinauTz).Date - utcTime.Date).TotalDays);
                        }
                        catch (TimeZoneNotFoundException)
                        {
                            // Fallback to UTC+3 offset without DST
                            var chisinauOffset = TimeSpan.FromHours(3);
                            var utcTime = dateTime - chisinauOffset;
                            return Math.Max(0, (int)(DateTime.UtcNow.Date - utcTime.Date).TotalDays);
                        }
                    }
                }
            }
            catch
            {
            }
            return -1;
        }

        private async Task<(string? html, int statusCode)> FetchPageWithRetryAsync(string url, string referer, int maxRetries, CancellationToken cancellationToken)
        {
            int lastStatus = 0;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 0)
                {
                    // If we got throttled or blocked, wait longer (exponential backoff: 15s, then 30s)
                    int delayMs = (lastStatus == 429 || lastStatus == 403) 
                        ? (attempt == 1 ? 15000 : 30000) 
                        : _random.Next(3000, 5000) * attempt;
                    
                    await Task.Delay(delayMs, cancellationToken);
                }
                try
                {
                    var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                    httpRequestMessage.Headers.TryAddWithoutValidation("Referer", referer);
                    
                    // Randomize user-agent for EACH retry attempt to bypass fingerprint blocks
                    string randomUserAgent = _userAgents[_random.Next(_userAgents.Length)];
                    httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", randomUserAgent);
                    
                    // Add modern browser headers
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                    httpRequestMessage.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                    httpRequestMessage.Headers.TryAddWithoutValidation("Accept-Language", "ro-RO,ro;q=0.9,ru;q=0.8,en-US;q=0.7,en;q=0.6");

                    var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
                    lastStatus = (int)httpResponseMessage.StatusCode;
                    
                    if (lastStatus == 429 || lastStatus == 403)
                    {
                        continue; // will delay and try again with a different User-Agent
                    }
                    return (html: await httpResponseMessage.Content.ReadAsStringAsync(), statusCode: lastStatus);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception) when (attempt < maxRetries)
                {
                    // Ignore transient exceptions and retry
                }
                catch
                {
                    // Final catch block
                }
            }
            return (html: null, statusCode: lastStatus);
        }

        private static bool IsBlockedResponse(string html, int statusCode)
        {
            if (statusCode == 403 || statusCode == 429)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return true;
            }

            string lowerHtml = html.ToLowerInvariant();
            string[] blockedSnippets = new[]
            {
                "cf-browser-verification",
                "challenge-running",
                "just a moment",
                "checking your browser",
                "ddos protection by cloudflare",
                "id=\"challenge-form\"",
                "window._cf_challenge_",
                "please enable javascript and cookies to continue",
                "access denied",
                "forbidden",
                "error 403",
                "error 429",
                "service temporarily unavailable",
                "unusual traffic"
            };

            foreach (var snippet in blockedSnippets)
            {
                if (lowerHtml.Contains(snippet, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (html.Length < 1000)
            {
                return true;
            }

            return false;
        }
    }
}
