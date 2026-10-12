using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class AIVisionService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string[] _candidateModels = new[]
        {
            "gemini-2.5-flash",
            "gemini-2.0-flash",
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
                        Log.Debug(ex, "[AIVisionService] Ошибка загрузки картинки {Url}", url);
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
                        new { parts = parts }
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
                            var jsonResponse = JObject.Parse(responseString);
                            var textToken = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"];
                            if (textToken != null)
                            {
                                return textToken.ToString().Trim();
                            }
                        }

                        lastError = $"[Модель {modelName}] HTTP {(int)response.StatusCode}: {responseString}";
                        Log.Warning("[AIVisionService] Модель {ModelName} вернула {StatusCode}, пробуем следующую...", modelName, response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        lastError = $"[Модель {modelName}] {ex.Message}";
                        Log.Warning(ex, "[AIVisionService] Ошибка вызова модели {ModelName}", modelName);
                    }
                }

                return $"Ошибка API ИИ: {lastError}";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AIVisionService] Ошибка при визуальном анализе");
                return $"Ошибка при визуальном анализе: {ex.Message}";
            }
        }

        public async Task<LlmAnalysisResponse> AnalyzeListingStructuredAsync(string listingContext, List<string> imageUrls, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return new LlmAnalysisResponse
                {
                    IsHotDeal = false,
                    CalculatedPriority = 50.0,
                    ReasoningSteps = "Ошибка: Ключ Gemini API не настроен."
                };
            }

            try
            {
                string promptTemplate = LoadEvaluatorPromptTemplate();
                string promptText;

                if (promptTemplate.Contains("{TITLE}") || promptTemplate.Contains("{DESCRIPTION}"))
                {
                    promptText = promptTemplate
                        .Replace("{TITLE}", listingContext ?? string.Empty)
                        .Replace("{DESCRIPTION}", string.Empty);
                }
                else
                {
                    promptText = $"{promptTemplate}\n\nListing Context:\n{listingContext}";
                }

                var parts = new List<object>
                {
                    new { text = promptText }
                };

                // Add up to 3 images if provided
                if (imageUrls != null && imageUrls.Count > 0)
                {
                    foreach (var url in imageUrls.Take(3))
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
                            Log.Debug(ex, "[AIVisionService] Не удалось загрузить фото для структурированного анализа: {Url}", url);
                        }
                    }
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = parts }
                    },
                    generationConfig = new
                    {
                        response_mime_type = "application/json",
                        temperature = 0.2
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
                            var jsonResponse = JObject.Parse(responseString);
                            var textToken = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"];
                            if (textToken != null)
                            {
                                string rawJson = CleanJsonString(textToken.ToString());
                                var result = JsonConvert.DeserializeObject<LlmAnalysisResponse>(rawJson);
                                if (result != null)
                                {
                                    // Post-process defaults if LLM did not fill them
                                    if (result.CalculatedPriority <= 0.0)
                                    {
                                        double priority = (result.ConditionScore * 10.0)
                                            - (result.RepairCostMdl > 0 ? 25.0 : 0.0)
                                            - (result.IsCommercial ? 30.0 : 0.0)
                                            - (result.IsStolen ? 100.0 : 0.0);
                                        result.CalculatedPriority = Math.Max(0.0, Math.Min(100.0, priority));
                                    }

                                    if (!result.IsHotDeal && result.ConditionScore >= 8 && result.RepairCostMdl == 0 && !result.IsStolen && !result.IsCommercial)
                                    {
                                        result.IsHotDeal = true;
                                    }

                                    if (string.IsNullOrWhiteSpace(result.ReasoningSteps))
                                    {
                                        result.ReasoningSteps = $"Оценка: {result.ConditionScore}/10. Ремонт: {result.RepairCostMdl} MDL. Дефекты: {(result.Defects?.Count > 0 ? string.Join(", ", result.Defects) : "нет")}.";
                                    }

                                    return result;
                                }
                            }
                        }

                        lastError = $"[Модель {modelName}] HTTP {(int)response.StatusCode}: {responseString}";
                        Log.Warning("[AIVisionService.Structured] Модель {ModelName} вернула {StatusCode}, пробуем следующую...", modelName, response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        lastError = $"[Модель {modelName}] {ex.Message}";
                        Log.Warning(ex, "[AIVisionService.Structured] Ошибка вызова модели {ModelName}", modelName);
                    }
                }

                return new LlmAnalysisResponse
                {
                    IsHotDeal = false,
                    CalculatedPriority = 50.0,
                    ReasoningSteps = $"Ошибка структурированного анализа: {lastError}"
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AIVisionService] Исключение при выполнении AnalyzeListingStructuredAsync");
                return new LlmAnalysisResponse
                {
                    IsHotDeal = false,
                    CalculatedPriority = 50.0,
                    ReasoningSteps = $"Исключение: {ex.Message}"
                };
            }
        }

        private static string CleanJsonString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "{}";
            string cleaned = text.Trim();

            // Strip ```json ... ``` wrapper if present
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(7);
            }
            else if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }

            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }

            return cleaned.Trim();
        }

        private static string LoadEvaluatorPromptTemplate()
        {
            try
            {
                string[] possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", "evaluator_prompt.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Prompts", "evaluator_prompt.txt"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Prompts", "evaluator_prompt.txt")
                };

                foreach (var p in possiblePaths)
                {
                    if (File.Exists(p))
                    {
                        return File.ReadAllText(p);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[AIVisionService] Не удалось загрузить evaluator_prompt.txt, используется fallback-промпт");
            }

            return @"You are an expert AI agent specializing in assessing used smartphones listed on secondary marketplaces. Your task is to analyze the title and description of a listing and output a structured JSON object detailing the phone's properties, defects, and repair estimates.

Analyze the following listing text:
---
Title: {TITLE}
Description: {DESCRIPTION}
---

Your response must be a single valid JSON block containing these fields:
1. 'brand' (string)
2. 'model' (string)
3. 'storage_gb' (int)
4. 'defects' (array of strings)
5. 'repair_cost_mdl' (int)
6. 'is_stolen' (boolean)
7. 'is_commercial' (boolean)
8. 'condition_score' (int, 1-10)
9. 'is_hot_deal' (boolean)
10. 'calculated_priority' (number, 0-100)
11. 'reasoning_steps' (string)

Output ONLY valid JSON.";
        }
    }
}
