using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace SmartphoneMonitor.Services
{
    public class PromoSyncAgent : BackgroundService
    {
        private readonly RetailPromoService _promoService;

        public PromoSyncAgent(RetailPromoService promoService)
        {
            _promoService = promoService ?? throw new ArgumentNullException(nameof(promoService));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            TimeSpan interval = TimeSpan.FromHours(1);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var map = _promoService.LoadPromoPriceMap();
                    System.Diagnostics.Debug.WriteLine($"[PromoSyncAgent] Loaded {map?.Count ?? 0} promo keys.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PromoSyncAgent] Error: {ex.Message}");
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
        }
    }
}
