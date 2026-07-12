using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class TelegramNotificationService
    {
        private readonly HttpClient _httpClient;
        private int _lastUpdateId = 0;
        private CancellationTokenSource? _pollingCts;

        public event Action<string, string>? BlacklistRequested;

        public TelegramNotificationService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<bool> SendTestMessageAsync(string token, string chatId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            {
                return false;
            }

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";
                var payload = new
                {
                    chat_id = chatId,
                    text = "🧪 <b>SmartphoneMonitor:</b> Тестовое сообщение. Связь с ботом успешно настроена! 🚀",
                    parse_mode = "HTML"
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendHotDealNotificationAsync(string token, string chatId, HotDeal deal)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            {
                return false;
            }

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";

                var sb = new StringBuilder();
                sb.AppendLine($"⭐ <b>ГОРЯЧАЯ СДЕЛКА! [Рекомендация: {deal.RecommendationScore:F0}/100]</b>");
                sb.AppendLine($"<b>Модель:</b> {EscapeHtml(deal.Brand)} {EscapeHtml(deal.Title)}");
                sb.AppendLine($"<b>Цена:</b> {deal.PriceValue:F0} MDL (Рынок: {deal.BrandMedian:F0} MDL, Скидка: {deal.DiscountPercent:F0}%)");

                if (deal.NetProfitMargin > 0m)
                {
                    sb.AppendLine($"<b>Чистая маржа:</b> +{deal.NetProfitMargin:F0} MDL");
                }
                if (deal.RepairCost > 0m)
                {
                    sb.AppendLine($"<b>Стоимость ремонта:</b> {deal.RepairCost:F0} MDL");
                }
                if (deal.Defects != null && deal.Defects.Count > 0)
                {
                    sb.AppendLine($"<b>Дефекты:</b> {EscapeHtml(string.Join(", ", deal.Defects))}");
                }
                if (deal.StorageGB > 0)
                {
                    sb.AppendLine($"<b>Память:</b> {deal.StorageGB} GB");
                }
                if (deal.BatteryHealth > 0)
                {
                    sb.AppendLine($"<b>АКБ:</b> {deal.BatteryHealth}%");
                }
                if (deal.Views > 0)
                {
                    sb.AppendLine($"<b>Просмотры:</b> 👁 {deal.Views}");
                }
                if (!string.IsNullOrEmpty(deal.SellerName))
                {
                    sb.AppendLine($"<b>Продавец:</b> {EscapeHtml(deal.SellerName)}");
                }
                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                {
                    sb.AppendLine($"<b>Телефон:</b> <code>{deal.PhoneNumber}</code>");
                }

                var buttons = new List<object[]>();
                var row1 = new List<object>
                {
                    new { text = "🔗 Открыть на 999", url = deal.Url }
                };

                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                {
                    row1.Add(new { text = "📞 Позвонить", url = $"tel:{deal.PhoneNumber}" });
                }
                buttons.Add(row1.ToArray());

                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                {
                    buttons.Add(new[]
                    {
                        new { text = "🚫 В черный список", callback_data = $"blacklist:{deal.PhoneNumber}" }
                    });
                }

                var payload = new
                {
                    chat_id = chatId,
                    text = sb.ToString(),
                    parse_mode = "HTML",
                    reply_markup = new { inline_keyboard = buttons.ToArray() }
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void StartPolling(string token, string chatId)
        {
            StopPolling();
            _pollingCts = new CancellationTokenSource();
            var ct = _pollingCts.Token;

            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await PollUpdatesAsync(token, chatId, ct);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Telegram Polling Error]: {ex.Message}");
                    }
                    try
                    {
                        await Task.Delay(3000, ct);
                    }
                    catch (TaskCanceledException) { }
                }
            }, ct);
        }

        public void StopPolling()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private async Task PollUpdatesAsync(string token, string chatId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token)) return;

            string url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_lastUpdateId + 1}&timeout=10&limit=10";
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return;

            string content = await response.Content.ReadAsStringAsync(ct);
            var json = JObject.Parse(content);
            if (json["ok"]?.Value<bool>() != true) return;

            var updates = json["result"] as JArray;
            if (updates == null || updates.Count == 0) return;

            foreach (var update in updates)
            {
                int updateId = update["update_id"]?.Value<int>() ?? 0;
                if (updateId > _lastUpdateId)
                {
                    _lastUpdateId = updateId;
                }

                var callbackQuery = update["callback_query"];
                if (callbackQuery != null)
                {
                    string queryId = callbackQuery["id"]?.Value<string>() ?? string.Empty;
                    string data = callbackQuery["data"]?.Value<string>() ?? string.Empty;
                    var message = callbackQuery["message"];

                    if (data.StartsWith("blacklist:") && message != null)
                    {
                        string phone = data.Substring("blacklist:".Length);
                        string msgChatId = message["chat"]?["id"]?.ToString() ?? string.Empty;
                        int msgId = message["message_id"]?.Value<int>() ?? 0;
                        string originalText = message["text"]?.Value<string>() ?? string.Empty;

                        // Trigger blacklist callback in MainViewModel
                        BlacklistRequested?.Invoke(phone, "Добавлен из Telegram-бота");

                        // Answer Callback Query to show Telegram toast message
                        await AnswerCallbackQueryAsync(token, queryId, "Номер добавлен в ЧС и отфильтрован!");

                        // Edit original message text
                        await EditMessageTextAsync(token, msgChatId, msgId, originalText);
                    }
                }
            }
        }

        private async Task AnswerCallbackQueryAsync(string token, string callbackQueryId, string text)
        {
            try
            {
                string url = $"https://api.telegram.org/bot{token}/answerCallbackQuery";
                var payload = new
                {
                    callback_query_id = callbackQueryId,
                    text = text
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch { }
        }

        private async Task EditMessageTextAsync(string token, string chatId, int messageId, string originalText)
        {
            try
            {
                string url = $"https://api.telegram.org/bot{token}/editMessageText";
                string updatedText = $"🚫 <b>[НОМЕР ЗАБЛОКИРОВАН В ЧС]</b>\n\n{originalText}";

                var payload = new
                {
                    chat_id = chatId,
                    message_id = messageId,
                    text = updatedText,
                    parse_mode = "HTML"
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch { }
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
