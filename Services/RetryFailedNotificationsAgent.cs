using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SmartphoneMonitor.Services
{
    // Periodically retries failed notifications persisted by TelegramNotificationService
    public class RetryFailedNotificationsAgent : BackgroundService
    {
        private readonly TelegramNotificationService _notifier;
        private readonly string _failedQueuePath;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        public RetryFailedNotificationsAgent(TelegramNotificationService notifier)
        {
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _failedQueuePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "failed_notifications.log");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!File.Exists(_failedQueuePath))
                    {
                        await Task.Delay(_interval, stoppingToken);
                        continue;
                    }

                    string[] lines;
                    try
                    {
                        lines = File.ReadAllLines(_failedQueuePath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to read failed notifications file");
                        await Task.Delay(_interval, stoppingToken);
                        continue;
                    }

                    if (lines.Length == 0)
                    {
                        await Task.Delay(_interval, stoppingToken);
                        continue;
                    }

                    var remaining = new List<string>();
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var entry = JsonSerializer.Deserialize<FailedNotificationEntry>(line);
                            if (entry == null)
                            {
                                continue;
                            }

                            // Only retry if we have both token and chatId
                            if (!string.IsNullOrEmpty(entry.Token) && !string.IsNullOrEmpty(entry.ChatId))
                            {
                                try
                                {
                                    await _notifier.SendMessageAsync(entry.Token, entry.ChatId, entry.Text);
                                    Log.Information("Retried notification sent: {Text}", entry.Text);
                                }
                                catch
                                {
                                    // keep for next round
                                    remaining.Add(line);
                                }
                            }
                            else
                            {
                                // Old format without credentials — log and drop
                                Log.Warning("Dropping old-format failed notification without credentials: {Text}", entry.Text);
                            }
                        }
                        catch
                        {
                            // corrupted line — drop it
                        }
                    }

                    try
                    {
                        if (remaining.Count == 0)
                        {
                            File.Delete(_failedQueuePath);
                        }
                        else
                        {
                            File.WriteAllLines(_failedQueuePath, remaining);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to update failed notifications file");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "RetryFailedNotificationsAgent error");
                }

                try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        private class FailedNotificationEntry
        {
            public DateTime Timestamp { get; set; }
            public string Text { get; set; } = string.Empty;
            public string? Token { get; set; }
            public string? ChatId { get; set; }
        }
    }
}