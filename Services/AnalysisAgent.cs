using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class AnalysisAgent : BackgroundService
    {
        private readonly ChannelReader<Listing> _reader;
        private readonly ChannelWriter<HotDeal> _hotDealWriter;
        private readonly DataAnalysisService _analysisService;

        private readonly DatabaseService? _databaseService;

        public AnalysisAgent(Channel<Listing> listingChannel, Channel<HotDeal> hotDealChannel, DataAnalysisService analysisService, DatabaseService? databaseService = null)
        {
            _reader = listingChannel.Reader;
            _hotDealWriter = hotDealChannel.Writer;
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _databaseService = databaseService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            var buffer = new List<Listing>();
            TimeSpan maxBatchDelay = TimeSpan.FromSeconds(10);
            int maxBatchSize = 50;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var readTask = _reader.WaitToReadAsync(stoppingToken).AsTask();
                    await readTask;
                    while (_reader.TryRead(out var item))
                    {
                        buffer.Add(item);
                        if (buffer.Count >= maxBatchSize) break;
                    }

                    if (buffer.Count > 0)
                    {
                        try
                        {
                            var blacklistPhones = _databaseService?.GetBlacklist()?.Select(b => b.PhoneNumber).ToList() ?? new List<string>();
                            var blacklistLogins = _databaseService?.GetBlacklistLogins()?.Select(b => b.Login).ToList() ?? new List<string>();
                            var result = await Task.Run(() => _analysisService.Analyze(buffer, blacklistPhones, blacklistLogins), stoppingToken);
                            // push HotDeals to notification channel
                            if (result?.HotDeals != null)
                            {
                                foreach (var hd in result.HotDeals)
                                {
                                    while (!stoppingToken.IsCancellationRequested)
                                    {
                                        if (await _hotDealWriter.WaitToWriteAsync(stoppingToken))
                                        {
                                            if (_hotDealWriter.TryWrite(hd)) break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AnalysisAgent] Analysis error: {ex.Message}");
                        }
                        buffer.Clear();
                    }
                    else
                    {
                        await Task.Delay(maxBatchDelay, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AnalysisAgent] Error: {ex.Message}");
                    await Task.Delay(1000, stoppingToken);
                }
            }
            _hotDealWriter.Complete();
        }
    }
}
