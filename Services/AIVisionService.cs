using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

namespace SmartphoneMonitor.Services
{
    public class AIVisionService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string[] _candidateModels = new[]
        {
            "gemini-3.6-flash",
            "gemini-2.5-flash",
            "gemini-2.0-flash",
            "gemini-1.5-flash-latest",
            "gemini-1.5-flash"
        };

        public async Task<string> AnalyzeImagesAsync(List<string> imageUrls, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return "Ошибка: Ключ Gemini API не настроен.";
            }

            if (imageUrls == null || imageUrls.Count == 0)
            {
                return "Ошибка: Фотографии не найдены.";
            }

            try
            {
                // Take up to 3 images to save tokens and bandwidth
                var urlsToAnalyze = imageUrls.Take(3).ToList();
                var parts = new List<object>();

                parts.Add(new { text = "Ты — циничный и опытный перекупщик смартфонов. Твоя задача — найти подвох, проверить реальное состояние устройства и выявить дефекты. Если продавец заявляет состояние '9.9/10' или 'Идеал', не верь на слово: внимательно ищи сколы, вмятины, микроцарапины, выгорания OLED, следы переклейки стекол, неоригинальные дисплеи (толстые рамки) и следы вскрытия (сорванные винты). Если телефону 3-5 лет, сразу отмечай естественный износ. Отвечай кратко и емко: 1) Состояние корпуса и стекла; 2) Следы ремонта/вскрытия; 3) Итоговый вердикт перекупщика." });

                foreach (var url in urlsToAnalyze)
                {
                    try
                    {
                        byte[] imageBytes = await _httpClient.GetByteArrayAsync(url);
                        string base64Image = Convert.ToBase64String(imageBytes);

                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data = base64Image
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AIVisionService] Ошибка загрузки картинки {url}: {ex.Message}");
                    }
                }

                if (parts.Count <= 1)
                {
                    return "Ошибка: Не удалось загрузить ни одного изображения по ссылкам.";
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = parts
                        }
                    }
                };

                string jsonBody = JsonConvert.SerializeObject(requestBody);
                string lastError = string.Empty;

                foreach (var modelName in _candidateModels)
                {
                    try
                    {
                        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        string requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                        
                        var response = await _httpClient.PostAsync(requestUrl, content);
                        string responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                            string aiText = jsonResponse.candidates[0].content.parts[0].text;
                            return aiText.Trim();
                        }

                        lastError = $"[Модель {modelName}] HTTP {(int)response.StatusCode}: {responseString}";
                        System.Diagnostics.Debug.WriteLine($"[AIVisionService] Модель {modelName} вернула {response.StatusCode}, пробуем следующую...");
                    }
                    catch (Exception ex)
                    {
                        lastError = $"[Модель {modelName}] {ex.Message}";
                    }
                }

                return $"Ошибка API ИИ: {lastError}";
            }
            catch (Exception ex)
            {
                return $"Ошибка при визуальном анализе: {ex.Message}";
            }
        }

        public async Task<SmartphoneMonitor.Models.LlmAnalysisResponse> AnalyzeListingStructuredAsync(string listingContext, List<string> imageUrls, string apiKey)
        {
            string rawAnalysis = await AnalyzeImagesAsync(imageUrls, apiKey);
            try
            {
                var structured = JsonConvert.DeserializeObject<SmartphoneMonitor.Models.LlmAnalysisResponse>(rawAnalysis);
                if (structured != null && !string.IsNullOrEmpty(structured.ReasoningSteps))
                {
                    return structured;
                }
            }
            catch { }

            return new SmartphoneMonitor.Models.LlmAnalysisResponse
            {
                IsHotDeal = false,
                CalculatedPriority = 50.0,
                ReasoningSteps = rawAnalysis
            };
        }
    }
}
