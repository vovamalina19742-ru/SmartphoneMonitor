using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SmartphoneMonitor.Models;
using Serilog;

namespace SmartphoneMonitor.Services
{
    public class ScraperAgent : BackgroundService
    {
        private readonly WebScraperService _scraper;
        private readonly ChannelWriter<Listing> _writer;
        private readonly DatabaseService? _databaseService;

        public ScraperAgent(WebScraperService scraper, Channel<Listing> channel, DatabaseService? databaseService = null)
        {
            _scraper = scraper ?? throw new ArgumentNullException(nameof(scraper));
            _writer = channel.Writer;
            _databaseService = databaseService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            TimeSpan interval = TimeSpan.FromMinutes(5);
            while (!stoppingToken.IsCancellationRequested)
            {
                bool isAutoMonitoring = bool.TryParse(_databaseService?.GetSetting("IsAutoMonitoring", "false"), out bool auto) && auto;
                if (!isAutoMonitoring)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    var listings = await _scraper.ScrapeSmartphonesAsync(3, 19.0m, null, stoppingToken);
                    foreach (var l in listings)
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            if (await _writer.WaitToWriteAsync(stoppingToken))
                            {
                                if (_writer.TryWrite(l)) break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ScraperAgent] Background scrape error");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            _writer.Complete();
        }
    }
}
