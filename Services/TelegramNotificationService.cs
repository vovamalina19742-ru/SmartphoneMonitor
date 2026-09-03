using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class TelegramNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _failedQueuePath;
        private int _lastUpdateId = 0;
        private CancellationTokenSource? _pollingCts;

        public event Action<string, string>? BlacklistRequested;
        public event Action<string, string>? BlacklistLoginRequested;

        public TelegramNotificationService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _failedQueuePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "failed_notifications.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_failedQueuePath) ?? ".");
            }
            catch { }
        }

        public TelegramNotificationService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("telegram") ?? new HttpClient();
            _failedQueuePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "failed_notifications.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_failedQueuePath) ?? ".");
            }
            catch { }
        }

        public async Task<bool> SendTestMessageAsync(string token, string chatId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
                return false;

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

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Log.Warning("SendTestMessage failed (status {Status}): {Error}", response.StatusCode, errorBody);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SendTestMessageAsync exception");
                return false;
            }
        }

        public async Task<bool> SendTextMessageAsync(string token, string chatId, string messageHtml)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
                return false;

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";
                var payload = new
                {
                    chat_id = chatId,
                    text = messageHtml,
                    parse_mode = "HTML",
                    disable_web_page_preview = true
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Log.Warning("SendTextMessage failed (status {Status}): {Error}", response.StatusCode, errorBody);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SendTextMessageAsync exception");
                return false;
            }
        }

        public async Task<bool> SendHotDealNotificationAsync(string token, string chatId, HotDeal deal)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
                return false;

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";

                var sb = new StringBuilder();
                sb.AppendLine($"⭐ <b>ГОРЯЧАЯ СДЕЛКА! [Рекомендация: {deal.RecommendationScore:F0}/100]</b>");
                sb.AppendLine($"<b>Модель:</b> {EscapeHtml(deal.Brand)} {EscapeHtml(deal.Title)}");
                string marketType = deal.IsNew ? "Рынок новых" : "Рынок б/у";
                sb.AppendLine($"<b>Цена:</b> {deal.PriceValue:F0} MDL ({marketType}: {deal.BrandMedian:F0} MDL, Скидка: {deal.DiscountPercent:F0}%)");

                if (deal.QuantEvaluation != null)
                {
                    sb.AppendLine($"<b>📊 Квант-оценка:</b> {EscapeHtml(deal.QuantEvaluation.BadgeText)} (Индекс: {deal.QuantEvaluation.QuantScore}/100)");
                    if (deal.QuantEvaluation.FairValuePrice > 0)
                        sb.AppendLine($"<b>Fair Value (Справедливая):</b> {deal.QuantEvaluation.FairValuePrice:F0} MDL");
                }

                if (deal.NetProfitMargin > 0m)
                    sb.AppendLine($"<b>Чистая маржа:</b> +{deal.NetProfitMargin:F0} MDL");

                if (deal.RecommendedResellPrice > 0m)
                    sb.AppendLine($"<b>Рекомендованная цена перепродажи:</b> {deal.RecommendedResellPrice:F0} MDL");

                if (!string.IsNullOrEmpty(deal.AiReasoning))
                {
                    sb.AppendLine();
                    sb.AppendLine(EscapeHtml(deal.AiReasoning));
                }

                if (deal.RepairCost > 0m)
                    sb.AppendLine($"<b>Стоимость ремонта:</b> {deal.RepairCost:F0} MDL");

                if (deal.Defects != null && deal.Defects.Count > 0)
                    sb.AppendLine($"<b>Дефекты:</b> {EscapeHtml(string.Join(", ", deal.Defects))}");

                if (deal.StorageGB > 0)
                    sb.AppendLine($"<b>Память:</b> {deal.StorageGB} GB");

                if (deal.BatteryHealth > 0)
                    sb.AppendLine($"<b>АКБ:</b> {deal.BatteryHealth}%");

                if (deal.Views > 0)
                    sb.AppendLine($"<b>Просмотры:</b> 👁 {deal.Views}");

                if (!string.IsNullOrEmpty(deal.SellerName))
                    sb.AppendLine($"<b>Продавец:</b> {EscapeHtml(deal.SellerName)}");

                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                    sb.AppendLine($"<b>Телефон:</b> <code>{deal.PhoneNumber}</code>");

                var buttons = new List<object[]>();
                var row1 = new List<object>
                {
                    new { text = "🔗 Открыть на 999", url = deal.Url }
                };

                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                    row1.Add(new { text = "📞 Позвонить", url = $"tel:{deal.PhoneNumber}" });

                buttons.Add(row1.ToArray());

                if (!string.IsNullOrEmpty(deal.PhoneNumber))
                {
                    buttons.Add(new[]
                    {
                        new { text = "🚫 Заблокировать телефон", callback_data = $"blacklist:{deal.PhoneNumber}" }
                    });
                }

                if (!string.IsNullOrEmpty(deal.AuthorLogin))
                {
                    buttons.Add(new[]
                    {
                        new { text = $"🚫 Заблокировать логин ({deal.AuthorLogin})", callback_data = $"blacklist_login:{deal.AuthorLogin}" }
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

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Log.Warning("SendHotDealNotification failed (status {Status}): {Error}. Retrying plain text...", response.StatusCode, errorBody);

                    var fallbackPayload = new
                    {
                        chat_id = chatId,
                        text = sb.ToString().Replace("<b>", "").Replace("</b>", "").Replace("<code>", "").Replace("</code>", "").Replace("<i>", "").Replace("</i>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&"),
                        reply_markup = new { inline_keyboard = buttons.ToArray() }
                    };
                    var fallbackContent = new StringContent(JsonConvert.SerializeObject(fallbackPayload), Encoding.UTF8, "application/json");
                    var fallbackResp = await _httpClient.PostAsync(url, fallbackContent);
                    return fallbackResp.IsSuccessStatusCode;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SendHotDealNotificationAsync exception");
                return false;
            }
        }

        public async Task<bool> SendListingAlertAsync(string token, string chatId, ListingStateEvent evt)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId) || evt == null)
                return false;

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";
                var sb = new StringBuilder();

                if (evt.Type == ListingEventType.NewListing)
                {
                    sb.AppendLine("🆕 <b>Новое объявление!</b>");
                    sb.AppendLine($"📱 <b>Модель:</b> {EscapeHtml(evt.Brand)} {EscapeHtml(evt.Title)}");
                    sb.AppendLine($"💰 <b>Цена:</b> {evt.CurrentPrice:N0} MDL");
                    sb.AppendLine($"🔗 <a href=\"{evt.Url}\">Открыть на сайте</a>");
                }
                else
                {
                    decimal oldPrice = evt.OldPrice ?? evt.CurrentPrice;
                    decimal diff = evt.PriceDiff;
                    double percent = evt.PriceDiffPercent;

                    sb.AppendLine("📉 <b>Цена снижена!</b>");
                    sb.AppendLine($"📱 <b>Модель:</b> {EscapeHtml(evt.Brand)} {EscapeHtml(evt.Title)}");
                    sb.AppendLine($"🏷 <b>Старая цена:</b> <s>{oldPrice:N0}</s> MDL");
                    sb.AppendLine($"🔥 <b>Новая цена:</b> <b>{evt.CurrentPrice:N0} MDL</b> (-{diff:N0} MDL / <b>-{percent:F1}%</b>)");
                    sb.AppendLine($"🔗 <a href=\"{evt.Url}\">Открыть на сайте</a>");
                }

                var buttons = new List<object[]>
                {
                    new object[]
                    {
                        new { text = "🔗 Открыть на 999.md", url = evt.Url }
                    }
                };

                var payload = new
                {
                    chat_id = chatId,
                    text = sb.ToString(),
                    parse_mode = "HTML",
                    disable_web_page_preview = false,
                    reply_markup = new { inline_keyboard = buttons.ToArray() }
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    Log.Warning("SendListingAlertAsync failed ({Code}): {Error}", response.StatusCode, err);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SendListingAlertAsync exception");
                return false;
            }
        }

        public Task SendMessageAsync(string text)
        {
            Log.Warning("Telegram not configured — cannot send: {Text}", text);
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(string token, string chatId, string text)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            {
                Log.Warning("Telegram not configured — skipping notification: {Text}", text);
                return;
            }

            string url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new { chat_id = chatId, text = text, parse_mode = "HTML" };

            int maxAttempts = 3;
            int attempt = 0;
            while (attempt < maxAttempts)
            {
                attempt++;
                try
                {
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var resp = await _httpClient.PostAsync(url, content);
                    if (resp.IsSuccessStatusCode)
                    {
                        Log.Debug("Telegram message sent on attempt {Attempt}", attempt);
                        return;
                    }
                    else
                    {
                        string errorBody = await resp.Content.ReadAsStringAsync();
                        Log.Warning("Telegram send failed (status {Status}) on attempt {Attempt}: {Error}", resp.StatusCode, attempt, errorBody);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Telegram send error on attempt {Attempt}", attempt);
                }

                await Task.Delay(500 * attempt);
            }

            // All attempts failed — persist to failed queue file WITH token and chatId
            try
            {
                string maskedToken = !string.IsNullOrEmpty(token) && token.Length > 8 ? token.Substring(0, 4) + "***" : "***";
                var entry = new FailedNotification { Timestamp = DateTime.UtcNow, Text = text, Token = maskedToken, ChatId = chatId };
                var line = System.Text.Json.JsonSerializer.Serialize(entry);
                await File.AppendAllTextAsync(_failedQueuePath, line + Environment.NewLine);
                Log.Information("Persisted failed Telegram notification to queue: {Path}", _failedQueuePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to persist Telegram notification");
            }
        }

        public async Task SendMessageWithFullCredentialsAsync(string token, string chatId, string text)
        {
            await SendMessageAsync(token, chatId, text);
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

                        BlacklistRequested?.Invoke(phone, "Добавлен из Telegram-бота");

                        await AnswerCallbackQueryAsync(token, queryId, "Номер добавлен в ЧС и отфильтрован!");
                        await EditMessageTextAsync(token, msgChatId, msgId, originalText);
                    }
                    else if (data.StartsWith("blacklist_login:") && message != null)
                    {
                        string login = data.Substring("blacklist_login:".Length);
                        string msgChatId = message["chat"]?["id"]?.ToString() ?? string.Empty;
                        int msgId = message["message_id"]?.Value<int>() ?? 0;
                        string originalText = message["text"]?.Value<string>() ?? string.Empty;

                        BlacklistLoginRequested?.Invoke(login, "Добавлен из Telegram-бота");

                        await AnswerCallbackQueryAsync(token, queryId, "Логин добавлен в ЧС и отфильтрован!");
                        await EditMessageTextAsync(token, msgChatId, msgId, originalText, isLogin: true, blockedItem: login);
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

        private async Task EditMessageTextAsync(string token, string chatId, int messageId, string originalText, bool isLogin = false, string blockedItem = "")
        {
            try
            {
                string url = $"https://api.telegram.org/bot{token}/editMessageText";
                string prefix = isLogin
                    ? $"🚫 <b>[ЛОГИН {blockedItem} ЗАБЛОКИРОВАН В ЧС]</b>"
                    : "🚫 <b>[НОМЕР ЗАБЛОКИРОВАН В ЧС]</b>";
                string updatedText = $"{prefix}\n\n{originalText}";

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
            return text.Replace("&", "&" + "amp;").Replace("<", "&" + "lt;").Replace(">", "&" + "gt;");
        }

        private class FailedNotification
        {
            public DateTime Timestamp { get; set; }
            public string Text { get; set; } = string.Empty;
            public string? Token { get; set; }
            public string? ChatId { get; set; }
        }
    }
}