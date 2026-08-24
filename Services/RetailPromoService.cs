using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class RetailPromoService
    {
        private readonly ModelParserService _modelParser = new ModelParserService();
        private IDictionary<string, List<RetailPromoPrice>>? _cachedMap;
        private DateTime _lastLoadTime = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        private static readonly object _cacheLock = new object();

        public IDictionary<string, List<RetailPromoPrice>> LoadPromoPriceMap()
        {
            // Return cached map if still fresh
            if (_cachedMap != null && (DateTime.UtcNow - _lastLoadTime) < _cacheDuration)
            {
                return _cachedMap;
            }

            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedMap != null && (DateTime.UtcNow - _lastLoadTime) < _cacheDuration)
                {
                    return _cachedMap;
                }

                var retailPrices = LoadRetailPromoPrices();
                _cachedMap = BuildPromoMap(retailPrices);
                _lastLoadTime = DateTime.UtcNow;
                return _cachedMap;
            }
        }

        /// <summary>
        /// Forces a fresh reload of promo prices from disk, bypassing cache.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedMap = null;
                _lastLoadTime = DateTime.MinValue;
            }
        }

        public IDictionary<string, List<RetailPromoPrice>> BuildPromoMap(IEnumerable<RetailPromoPrice> retailPrices)
        {
            var promoMap = new Dictionary<string, List<RetailPromoPrice>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rp in retailPrices)
            {
                string model = _modelParser.ExtractModel(rp.Name, rp.Brand);

                // exact key with storage (sanitized)
                string keyExact = PromoKeyHelper.SanitizeKey(rp.Brand, model, rp.StorageGB);
                AddToMap(promoMap, keyExact, rp);
                Log.Debug("Added promo key {Key} -> {Shop} {Name} {Price}", keyExact, rp.Shop, rp.Name, rp.Price);

                // general model key without storage (storage placeholder 0)
                string keyGeneral0 = PromoKeyHelper.SanitizeKey(rp.Brand, model, 0);
                AddToMap(promoMap, keyGeneral0, rp);

                // alternative general key without explicit storage suffix
                string keyGeneral = PromoKeyHelper.SanitizeKey(rp.Brand, model);
                AddToMap(promoMap, keyGeneral, rp);
                Log.Debug("Added promo key {Key} -> {Shop} {Name} {Price}", keyGeneral, rp.Shop, rp.Name, rp.Price);

                // brand-only entries are left implicit; ListingEvaluationService will scan by prefix
            }

            Log.Information("Built promo map with {KeyCount} keys from {PromoCount} promos", promoMap.Count, retailPrices.Count());
            return promoMap;
        }

        private static void AddToMap(IDictionary<string, List<RetailPromoPrice>> map, string key, RetailPromoPrice rp)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<RetailPromoPrice>();
                map[key] = list;
            }
            list.Add(rp);
        }

        private List<RetailPromoPrice> LoadRetailPromoPrices()
        {
            var list = new List<RetailPromoPrice>();
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports", "retail_promo_prices.json");
                if (!File.Exists(path))
                {
                    path = Path.Combine(Directory.GetCurrentDirectory(), "reports", "retail_promo_prices.json");
                }

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var parsed = JsonConvert.DeserializeObject<List<RetailPromoPrice>>(json);
                    if (parsed != null)
                    {
                        list = parsed;
                    }
                }
            }
            catch
            {
            }

            return list;
        }

    }
}
