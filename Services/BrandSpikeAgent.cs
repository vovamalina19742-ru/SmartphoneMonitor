using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SmartphoneMonitor.Services
{
    // Detects sudden increases in promo matches per brand and logs/alerts.
    public class BrandSpikeAgent : BackgroundService
    {
        private readonly MetricsService _metrics;
        private readonly MetricsRepository _repo;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
        private readonly Dictionary<string, List<(DateTime ts, long value)>> _history = new Dictionary<string, List<(DateTime, long)>>(StringComparer.OrdinalIgnoreCase);
        // configuration
        private readonly TimeSpan _recentWindow = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _baselineWindow = TimeSpan.FromHours(6);
        private readonly double _minFoldIncrease = 3.0; // recent_rate > baseline_rate * fold
        private readonly long _minAbsoluteIncrease = 5; // minimal absolute delta

        private readonly TelegramNotificationService _notifier;

        public BrandSpikeAgent(MetricsService metrics, TelegramNotificationService notifier, MetricsRepository repo)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // seed history from repo on first run
                    if (_history.Count == 0)
                    {
                        try
                        {
                            var rows = _repo.QueryRecent("promo_matches_brand_", _baselineWindow);
                            foreach (var r in rows)
                            {
                                var key = r.name.Substring("promo_matches_brand_".Length);
                                if (!_history.TryGetValue(key, out var list)) { list = new List<(DateTime, long)>(); _history[key] = list; }
                                list.Add((r.ts.ToUniversalTime(), r.value));
                            }
                        }
                        catch { }
                    }

                    var counters = _metrics.GetCountersByPrefix("promo_matches_brand_");
                    DateTime now = DateTime.UtcNow;
                    foreach (var kv in counters)
                    {
                        var brandKey = kv.Key.Substring("promo_matches_brand_".Length);
                        long total = kv.Value;

                        if (!_history.TryGetValue(brandKey, out var list))
                        {
                            list = new List<(DateTime, long)>();
                            _history[brandKey] = list;
                        }

                        list.Add((now, total));

                        // prune old
                        DateTime pruneBefore = now - _baselineWindow - TimeSpan.FromMinutes(5);
                        list.RemoveAll(p => p.ts < pruneBefore);

                        // compute recent and baseline values
                        DateTime recentFrom = now - _recentWindow;
                        DateTime baselineFrom = now - _baselineWindow;

                        // Get the snapshot value at the start of the recent window (last value before recentFrom)
                        long beforeRecentValue = 0;
                        var beforeRecentPoints = list.Where(p => p.ts < recentFrom).OrderByDescending(p => p.ts).ToList();
                        if (beforeRecentPoints.Count > 0)
                            beforeRecentValue = beforeRecentPoints[0].value;

                        // Get the latest value (current total)
                        long currentValue = list.OrderByDescending(p => p.ts).First().value;

                        // The delta in the recent window
                        long absoluteDelta = currentValue - beforeRecentValue;
                        double recentRate = absoluteDelta / Math.Max(1.0, _recentWindow.TotalMinutes);

                        // Compute baseline per-minute rates from successive samples in baseline window
                        var baselineRates = new List<double>();
                        var baselinePoints = list.Where(p => p.ts >= baselineFrom && p.ts < recentFrom).OrderBy(p => p.ts).ToList();

                        // Also include the transition point into recent window
                        if (beforeRecentPoints.Count > 0 && baselinePoints.Count > 0)
                        {
                            var lastBaseline = beforeRecentPoints[0];
                            if (lastBaseline.ts >= baselineFrom)
                                baselinePoints.Add(lastBaseline);
                        }

                        baselinePoints = baselinePoints.OrderBy(p => p.ts).ToList();

                        for (int i = 1; i < baselinePoints.Count; i++)
                        {
                            var a = baselinePoints[i - 1];
                            var b = baselinePoints[i];
                            double minutes = (b.ts - a.ts).TotalMinutes;
                            if (minutes <= 0) continue;
                            double rate = (double)(b.value - a.value) / minutes;
                            if (rate < 0) continue;
                            baselineRates.Add(rate);
                        }

                        double baselineMean = 0.0;
                        double baselineStd = 0.0;
                        if (baselineRates.Count > 0)
                        {
                            baselineMean = baselineRates.Average();
                            double sumsq = baselineRates.Sum(r => (r - baselineMean) * (r - baselineMean));
                            baselineStd = Math.Sqrt(sumsq / baselineRates.Count);
                        }

                        bool spike = false;
                        if (absoluteDelta >= _minAbsoluteIncrease && recentRate > 0)
                        {
                            // Z-like test: recentRate > mean + k*std, or if no baseline but large increase
                            double zThreshold = baselineMean + 3.0 * baselineStd; // 3-sigma
                            if (baselineRates.Count >= 3)
                            {
                                if (recentRate > zThreshold) spike = true;
                            }
                            else
                            {
                                // fallback to fold comparison
                                double baselineRate = baselineMean;
                                double fold = baselineRate > 0 ? recentRate / baselineRate : (recentRate > 0 ? double.PositiveInfinity : 0.0);
                                if (fold >= _minFoldIncrease || double.IsInfinity(fold)) spike = true;
                            }
                        }

                        if (spike)
                        {
                            Log.Information("Brand spike detected: {Brand} absoluteDelta={Delta} recentRate={RecentRate:F2}/min baselineMean={BaselineMean:F2}/std={Std:F2} total={Total}", brandKey, absoluteDelta, recentRate, baselineMean, baselineStd, total);
                            try
                            {
                                string msg = $"🚨 Brand spike: *{brandKey}* — +{absoluteDelta} matches (rate {recentRate:F1}/min, baseline {baselineMean:F1}/min)";
                                _ = _notifier.SendMessageAsync(msg);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "BrandSpikeAgent error");
                }

                try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
