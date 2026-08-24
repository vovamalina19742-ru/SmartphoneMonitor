using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SmartphoneMonitor.Models;

using Serilog;

namespace SmartphoneMonitor.Services
{
    public class NotificationAgent : BackgroundService
    {
        private readonly ChannelReader<HotDeal> _reader;
        private readonly TelegramNotificationService? _telegramService;
        private readonly DatabaseService? _databaseService;

        public NotificationAgent(Channel<HotDeal> channel, TelegramNotificationService? telegramService = null, DatabaseService? databaseService = null)
        {
            _reader = channel.Reader;
            _telegramService = telegramService;
            _databaseService = databaseService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var hot in _reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    Log.Information("[NotificationAgent] Processing HotDeal: {Title} | {Brand} | {Price} MDL | Score={Score}", hot.Title, hot.Brand, hot.PriceValue, hot.RecommendationScore);

                    if (_telegramService != null && _databaseService != null)
                    {
                        bool telegramEnabled = bool.TryParse(_databaseService.GetSetting("TelegramEnabled", "false"), out bool tgEnabled) && tgEnabled;
                        string token = _databaseService.GetSetting("TelegramToken", string.Empty);
                        string chatId = _databaseService.GetSetting("TelegramChatId", string.Empty);

                        if (telegramEnabled && !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(chatId))
                        {
                            await _telegramService.SendHotDealNotificationAsync(token, chatId, hot);
                        }
                        else
                        {
                            Log.Information("[NotificationAgent] Telegram notification skipped (TelegramEnabled={Tg}, TokenConfigured={HasToken})", telegramEnabled, !string.IsNullOrEmpty(token));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[NotificationAgent] Notification dispatch error");
                }
            }
        }
    }
}
