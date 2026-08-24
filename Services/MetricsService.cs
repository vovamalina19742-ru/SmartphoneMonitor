using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace SmartphoneMonitor.Services
{
    // Lightweight metrics exposition for Prometheus-compatible scraping.
    // Serves a simple text endpoint at http://localhost:9184/metrics
    public class MetricsService : BackgroundService
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentDictionary<string, long> _counters = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _gauges = new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (long count, double sum)> _histograms = new ConcurrentDictionary<string, (long, double)>(StringComparer.OrdinalIgnoreCase);

        private readonly MetricsRepository _repo;

        public MetricsService(MetricsRepository repo)
        {
            _repo = repo;
            _listener.Prefixes.Add("http://localhost:9184/");
        }

        public void Increment(string name, long value = 1)
        {
            _counters.AddOrUpdate(name, value, (_, old) => old + value);
            try
            {
                _repo?.InsertMetric(name, value);
            }
            catch { }
        }

        public long GetCounterValue(string name)
        {
            if (_counters.TryGetValue(name, out var v)) return v;
            return 0;
        }

        public IDictionary<string, long> GetCountersByPrefix(string prefix)
        {
            var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _counters)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) dict[kv.Key] = kv.Value;
            }
            return dict;
        }

        public void SetGauge(string name, double value)
        {
            _gauges[name] = value;
        }

        public void ObserveHistogram(string name, double value)
        {
            _histograms.AddOrUpdate(name, (1, value), (_, old) => (old.count + 1, old.sum + value));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // port might be in use or permissions denied; exit gracefully
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext ctx = null;
                try
                {
                    var getContext = _listener.GetContextAsync();
                    var completed = await Task.WhenAny(getContext, Task.Delay(1000, stoppingToken));
                    if (completed != getContext) continue;
                    ctx = getContext.Result;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    continue;
                }

                _ = Task.Run(() => HandleRequestAsync(ctx), stoppingToken);
            }

            try { _listener.Stop(); } catch { }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var res = ctx.Response;
                if (req.Url.AbsolutePath.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder();
                    foreach (var kv in _counters)
                    {
                        sb.AppendLine($"{kv.Key} {kv.Value}");
                    }
                    foreach (var kv in _gauges)
                    {
                        sb.AppendLine($"{kv.Key} {kv.Value}");
                    }
                    foreach (var kv in _histograms)
                    {
                        sb.AppendLine($"{kv.Key}_count {kv.Value.count}");
                        sb.AppendLine($"{kv.Key}_sum {kv.Value.sum}");
                    }

                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    res.ContentType = "text/plain; version=0.0.4";
                    res.ContentEncoding = Encoding.UTF8;
                    res.ContentLength64 = bytes.Length;
                    await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    res.OutputStream.Close();
                }
                else
                {
                    res.StatusCode = 404;
                    res.Close();
                }
            }
            catch { }
        }
    }
}
