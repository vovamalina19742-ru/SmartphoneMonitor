using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SmartphoneMonitor.Services
{
    public class ExchangeRateService
    {
        private readonly HttpClient _httpClient;

        public ExchangeRateService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        public async Task<decimal> GetEurToMdlRateAsync(decimal fallbackRate = 19.5m)
        {
            try
            {
                string dateStr = DateTime.Now.ToString("dd.MM.yyyy");
                string url = $"https://www.bnm.md/en/official_exchange_rates?get_xml=1&date={dateStr}";
                
                string xmlContent = await _httpClient.GetStringAsync(url);
                if (string.IsNullOrEmpty(xmlContent))
                {
                    return fallbackRate;
                }

                var doc = XDocument.Parse(xmlContent);
                var valuteElements = doc.Descendants("Valute");

                foreach (var valute in valuteElements)
                {
                    var charCode = valute.Element("CharCode")?.Value;
                    if (charCode == "EUR")
                    {
                        var valueStr = valute.Element("Value")?.Value;
                        if (!string.IsNullOrEmpty(valueStr) && decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                        {
                            return Math.Round(rate, 4);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ExchangeRateService error: " + ex.Message);
            }
            return fallbackRate;
        }
    }
}
