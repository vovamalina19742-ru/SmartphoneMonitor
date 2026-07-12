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

        public WebScraperService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseCookies = true,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30.0);
            
            // Set static stealth headers
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ro-RO,ro;q=0.9,ru;q=0.8,en-US;q=0.7,en;q=0.6");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
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

        public async Task<(string phone, string seller, int views, string description)> FetchPhoneAsync(string url, CancellationToken cancellationToken = default)
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
                    return (phone: string.Empty, seller: string.Empty, views: views, description: string.Empty);
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
                    text3 = Regex.Replace(text3, "[^\\d]", "");
                    if (text3.StartsWith("373"))
                    {
                        text3 = text3.Substring(3);
                    }
                    if (text3.StartsWith("0"))
                    {
                        text3 = text3.Substring(1);
                    }
                    text3 = "+373" + text3;
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

                return (phone: text3, seller: ownerLogin, views: views, description: description);
            }
            catch
            {
                return (phone: string.Empty, seller: string.Empty, views: 0, description: string.Empty);
            }
        }

        private async Task<List<Listing>> FetchListingsGraphQLAsync(int page, decimal eurToMdlRate, CancellationToken cancellationToken)
        {
            var listings = new List<Listing>();
            string json = $"{{\n  \"input\": {{\n    \"source\": \"AD_SOURCE_DESKTOP_REDESIGN\",\n    \"sort\": \"SORT_ADS_DATE_DESC\",\n    \"pagination\": {{\n      \"limit\": 78,\n      \"skip\": {(page - 1) * 78}\n    }},\n    \"filters\": [\n      {{\n        \"filterId\": 6,\n        \"features\": [\n          {{\n            \"featureId\": 2,\n            \"unit\": \"UNIT_MDL\",\n            \"range\": {{\n              \"min\": \"1000\",\n              \"max\": \"5000\"\n            }}\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 16,\n        \"features\": [\n          {{\n            \"featureId\": 1,\n            \"optionIds\": [776]\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 1084,\n        \"features\": [\n          {{\n            \"featureId\": 593,\n            \"optionIds\": [6370, 6371]\n          }}\n        ]\n      }},\n      {{\n        \"filterId\": 290,\n        \"features\": [\n          {{\n            \"featureId\": 7,\n            \"optionIds\": [12900]\n          }}\n        ]\n      }}\n    ],\n    \"subCategoryId\": 40\n  }}\n}}";
            var value = new JObject
            {
                ["operationName"] = "SearchAds",
                ["variables"] = JObject.Parse(json),
                ["query"] = "query SearchAds($input: Ads_SearchInput!, $locale: Common_Locale) {\n  searchAds(input: $input) {\n    ads {\n      id\n      title\n      price: feature(id: 2) {\n        id\n        type\n        value\n      }\n      images: feature(id: 14) {\n        id\n        type\n        value\n      }\n      author: feature(id: 795) {\n        id\n        type\n        value\n      }\n      condition: feature(id: 593) {\n        id\n        type\n        value\n      }\n      owner {\n        id\n        login\n        avatar\n        business {\n          plan\n        }\n      }\n      reseted(\n        input: {format: \"2 Jan. 2006, 15:04\", locale: $locale, timezone: \"Europe/Chisinau\", getDiff: false}\n      )\n      __typename\n    }\n    count\n    __typename\n  }\n}"
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
                    if (IsAccessoryOrPartOrOther(title))
                    {
                        continue;
                    }

                    string conditionText = "б/у";
                    try
                    {
                        var conditionVal = item["condition"]?["value"];
                        if (conditionVal != null)
                        {
                            string condStr = conditionVal.ToString();
                            if (condStr.Contains("6370"))
                            {
                                conditionText = "новый";
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

                    string brand = DetectBrand(title);
                    int storageGB = ExtractStorage(title);
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

                    string allText = (title + " " + (isShop ? "magazin shop" : "")).ToLowerInvariant();
                    bool flag2 = Array.Exists(Constants.CommercialMarkers, (string m) => allText.Contains(m, StringComparison.OrdinalIgnoreCase));
                    bool isUrgent = Array.Exists(Constants.UrgencyMarkers, (string m) => allText.Contains(m, StringComparison.OrdinalIgnoreCase));

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
                        IsCommercial = flag2 || isShop,
                        IsUrgent = isUrgent,
                        PostedDate = postedDate
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
                        var dateTime = new DateTime(year, monthVal, day, hour, minute, 0);
                        return Math.Max(0, (int)(DateTime.Now.Date - dateTime.Date).TotalDays);
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
                    await Task.Delay(_random.Next(3000, 5000) * attempt, cancellationToken);
                }
                try
                {
                    var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                    httpRequestMessage.Headers.TryAddWithoutValidation("Referer", referer);
                    httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", _userAgents[_random.Next(_userAgents.Length)]);
                    
                    // Add modern browser headers
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                    httpRequestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");

                    var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
                    lastStatus = (int)httpResponseMessage.StatusCode;
                    if (lastStatus == 429 || lastStatus == 403)
                    {
                        continue;
                    }
                    return (html: await httpResponseMessage.Content.ReadAsStringAsync(), statusCode: lastStatus);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception) when (attempt < maxRetries)
                {
                }
                catch
                {
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
            if (string.IsNullOrEmpty(html) || html.Length < 3000)
            {
                return true;
            }
            if (!html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) && 
                !html.Contains("challenge-running", StringComparison.OrdinalIgnoreCase) && 
                !html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) && 
                !html.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) && 
                !html.Contains("DDoS protection by Cloudflare", StringComparison.OrdinalIgnoreCase))
            {
                return html.Contains("id=\"challenge-form\"", StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        public static int ExtractStorage(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return 0;
            }
            var match = Regex.Match(title, "\\b(32|64|128|256|512)\\s*[Gg][Bb]\\b");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int storage))
            {
                return storage;
            }
            if (Regex.IsMatch(title, "\\b1\\s*[Tt][Bb]\\b"))
            {
                return 1024;
            }
            return 0;
        }

        public static string DetectBrand(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Другие";
            }
            string text = title.ToLowerInvariant();
            if (text.Contains("iphone") || text.Contains("apple"))
            {
                return "Apple";
            }
            if (text.Contains("samsung") || text.Contains("galaxy") || Regex.IsMatch(text, "\\bgalaxy\\s+[aszfm]\\d"))
            {
                return "Samsung";
            }
            if (text.Contains("redmi") || text.Contains("poco") || text.Contains("xiaomi"))
            {
                return "Xiaomi";
            }
            if (text.Contains("honor"))
            {
                return "Honor";
            }
            if (text.Contains("huawei") || text.Contains("p smart") || Regex.IsMatch(text, "\\b(mate\\s*\\d+|nova\\s*\\d+|p\\d{2}\\s*(pro|lite)?)\\b"))
            {
                return "Huawei";
            }
            if (text.Contains("realme"))
            {
                return "Realme";
            }
            if (text.Contains("oneplus") || text.Contains("one plus"))
            {
                return "OnePlus";
            }
            if (text.Contains("oppo"))
            {
                return "OPPO";
            }
            if (text.Contains("nokia"))
            {
                return "Nokia";
            }
            if (text.Contains("motorola") || text.Contains("moto g") || text.Contains("moto e") || text.Contains("moto "))
            {
                return "Motorola";
            }
            if (text.Contains("sony") || text.Contains("xperia"))
            {
                return "Sony";
            }
            if (text.Contains("vivo"))
            {
                return "Vivo";
            }
            if (text.Contains("tecno"))
            {
                return "Tecno";
            }
            if (text.Contains("infinix"))
            {
                return "Infinix";
            }
            if (Regex.IsMatch(text, "\\blg\\b"))
            {
                return "LG";
            }
            if (text.Contains("asus") || text.Contains("zenfone") || text.Contains("rog phone"))
            {
                return "Asus";
            }
            if (text.Contains("lenovo"))
            {
                return "Lenovo";
            }
            if (text.Contains("zte"))
            {
                return "ZTE";
            }
            if (text.Contains("alcatel"))
            {
                return "Alcatel";
            }
            if (text.Contains("meizu"))
            {
                return "Meizu";
            }
            if (text.Contains("google pixel") || text.Contains("pixel "))
            {
                return "Google";
            }
            if (text.Contains("nothing phone") || text.Contains("nothing"))
            {
                return "Nothing";
            }
            if (text.Contains("blackview") || text.Contains("oukitel") || text.Contains("ulefone") || text.Contains("doogee"))
            {
                return "Rugged";
            }
            return "Другие";
        }

        public static bool IsAccessoryOrPartOrOther(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;
            string t = title.ToLowerInvariant();

            string[] exclusionKeywords = new string[]
            {
                "чехол", "чехлы", "husa", "huse", "sticla", "sticle", "пленка", "пленки", "folie", "folii",
                "кабель", "cablul", "cablu", "incarcator", "incarcatoare", "casti", "наушники", "airpods",
                "запчасти", "запчасть", "piese", "piese de schimb", "razborca", "разборка", "разбор",
                "детали", "на детали", "дисплей", "ecran", "display", "cutie", "cutii", "коробка", "коробки",
                "box", "icloud blocat", "icloud locked", "блокирован", "блокированый", "locked", "bypass", "mdm",
                "на запчасти", "для запчастей", "pentru piese", "pentru piese de schimb", "blocat icloud",
                "зарядка", "зарядки", "зарядное", "камера", "плата", "платы", "материнская плата", "placa de baza",
                "шлейф", "корпус", "carcasa", "стекло заднее", "sticla spate",
                "watch", "часы", "ceas", "ceasuri", "smartwatch", "smart-watch", "fitbit", "mi band", "smart band",
                "ipad", "планшет", "tableta", "tablet", "стационарный", "домашний телефон", "телефон домашний",
                "telefon fix"
            };

            foreach (var kw in exclusionKeywords)
            {
                if (t.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
