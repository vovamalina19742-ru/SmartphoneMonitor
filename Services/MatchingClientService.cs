using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace SmartphoneMonitor.Services
{
    public class MatchingClientService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        private static readonly SemaphoreSlim _throttle = new SemaphoreSlim(4, 4);
        private const string BaseUrl = "http://127.0.0.1:8000";
        private static bool _isServerOffline = false;

        public static void Reset()
        {
            _isServerOffline = false;
        }

        public async Task<(string MatchedModel, double Score, string Status)?> MatchDeviceAsync(string title, string brand)
        {
            if (_isServerOffline)
            {
                return null;
            }

            await _throttle.WaitAsync();
            try
            {
                var payload = new
                {
                    title = title,
                    brand = brand ?? ""
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BaseUrl}/match", content);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(jsonResponse);

                string matchedModel = data["matched_model"]?.ToString() ?? "Другой";
                double score = data["score"]?.Value<double>() ?? 0.0;
                string status = data["status"]?.ToString() ?? "CheckRequired";

                return (matchedModel, score, status);
            }
            catch (Exception ex)
            {
                if (!_isServerOffline)
                {
                    Log.Warning(ex, "[MatchingClientService] Python-сервис сопоставления недоступен. Переход в автономный режим (Fallback).");
                    _isServerOffline = true;
                }
                return null;
            }
            finally
            {
                _throttle.Release();
            }
        }

        public async Task<(bool IsValid, string WarningMessage)?> ValidatePriceAsync(
            decimal currentPrice,
            decimal costPrice,
            decimal marketAverage,
            decimal proposedPrice)
        {
            if (_isServerOffline)
            {
                return null;
            }

            await _throttle.WaitAsync();
            try
            {
                var payload = new
                {
                    current_price = (double)currentPrice,
                    cost_price = (double)costPrice,
                    market_average = (double)marketAverage,
                    proposed_price = (double)proposedPrice
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BaseUrl}/validate", content);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(jsonResponse);

                bool isValid = data["is_valid"]?.Value<bool>() ?? false;
                string warningMessage = data["warning_message"]?.ToString() ?? "Ошибка валидации.";

                return (isValid, warningMessage);
            }
            catch (Exception ex)
            {
                if (!_isServerOffline)
                {
                    Log.Warning(ex, "[MatchingClientService] Python-сервис сопоставления недоступен. Переход в автономный режим (Fallback).");
                    _isServerOffline = true;
                }
                return null;
            }
            finally
            {
                _throttle.Release();
            }
        }

        public async Task<System.Collections.Generic.List<SmartphoneMonitor.Models.DemandArbitrageDeal>> MatchArbitrageAsync(
            System.Collections.Generic.IEnumerable<SmartphoneMonitor.Models.DemandListing> demands,
            System.Collections.Generic.IEnumerable<SmartphoneMonitor.Models.Listing> supplies,
            decimal minProfitMargin = 200m)
        {
            var resultDeals = new System.Collections.Generic.List<SmartphoneMonitor.Models.DemandArbitrageDeal>();
            if (demands == null || supplies == null) return resultDeals;

            var demandList = System.Linq.Enumerable.ToList(demands);
            var supplyList = System.Linq.Enumerable.ToList(supplies);

            if (demandList.Count == 0 || supplyList.Count == 0) return resultDeals;

            if (!_isServerOffline)
            {
                await _throttle.WaitAsync();
                try
                {
                    var reqPayload = new
                    {
                        demands = System.Linq.Enumerable.Select(demandList, d => new
                        {
                            id = d.Id,
                            title = d.Title,
                            budget_price = (double)d.BudgetPrice,
                            brand = d.Brand,
                            model = d.Model,
                            storage_gb = d.StorageGB
                        }),
                        supplies = System.Linq.Enumerable.Select(supplyList, s => new
                        {
                            id = s.Url,
                            title = s.Title,
                            price = (double)s.PriceValue,
                            url = s.Url,
                            brand = s.Brand,
                            model = s.Model,
                            storage_gb = s.StorageGB
                        }),
                        min_profit_margin = (double)minProfitMargin
                    };

                    string jsonPayload = JsonConvert.SerializeObject(reqPayload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{BaseUrl}/match_arbitrage", content);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResp = await response.Content.ReadAsStringAsync();
                        var data = JObject.Parse(jsonResp);
                        var dealsArray = data["deals"] as JArray;
                        if (dealsArray != null)
                        {
                            var demandMap = System.Linq.Enumerable.ToDictionary(demandList, d => d.Id, d => d);
                            var supplyMap = System.Linq.Enumerable.ToDictionary(supplyList, s => s.Url, s => s);

                            foreach (var dealObj in dealsArray)
                            {
                                string dId = dealObj["demand_id"]?.ToString() ?? "";
                                string sId = dealObj["supply_id"]?.ToString() ?? "";
                                double score = dealObj["match_score"]?.Value<double>() ?? 1.0;

                                if (demandMap.TryGetValue(dId, out var dem) && supplyMap.TryGetValue(sId, out var sup))
                                {
                                    resultDeals.Add(new SmartphoneMonitor.Models.DemandArbitrageDeal
                                    {
                                        DemandId = dem.Id,
                                        DemandTitle = dem.Title,
                                        DemandBudget = dem.BudgetPrice,
                                        DemandUrl = dem.Url,
                                        DemandDate = dem.DateAdded,
                                        DemandAuthor = dem.AuthorLogin,
                                        DemandPhone = dem.PhoneNumber,

                                        SupplyId = sup.Url,
                                        SupplyTitle = sup.Title,
                                        SupplyPrice = sup.PriceValue,
                                        SupplyUrl = sup.Url,
                                        SupplyBrand = sup.Brand,
                                        SupplyModel = sup.Model,
                                        SupplyStorageGB = sup.StorageGB,

                                        MatchScore = score
                                    });
                                }
                            }
                            return resultDeals;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_isServerOffline)
                    {
                        Log.Warning(ex, "[MatchingClientService] Python /match_arbitrage недоступен. Выполнение автономного C# арбитража.");
                        _isServerOffline = true;
                    }
                }
                finally
                {
                    _throttle.Release();
                }
            }

            // Fallback C# matching
            foreach (var dem in demandList)
            {
                foreach (var sup in supplyList)
                {
                    if (dem.BudgetPrice - sup.PriceValue >= minProfitMargin)
                    {
                        string dBrand = dem.Brand.ToLowerInvariant();
                        string sBrand = sup.Brand.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(dBrand) && dBrand == sBrand)
                        {
                            resultDeals.Add(new SmartphoneMonitor.Models.DemandArbitrageDeal
                            {
                                DemandId = dem.Id,
                                DemandTitle = dem.Title,
                                DemandBudget = dem.BudgetPrice,
                                DemandUrl = dem.Url,
                                DemandDate = dem.DateAdded,
                                DemandAuthor = dem.AuthorLogin,
                                DemandPhone = dem.PhoneNumber,

                                SupplyId = sup.Url,
                                SupplyTitle = sup.Title,
                                SupplyPrice = sup.PriceValue,
                                SupplyUrl = sup.Url,
                                SupplyBrand = sup.Brand,
                                SupplyModel = sup.Model,
                                SupplyStorageGB = sup.StorageGB,

                                MatchScore = 0.8
                            });
                        }
                    }
                }
            }

            return resultDeals;
        }
    }
}
