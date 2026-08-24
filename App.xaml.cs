using System;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;
using Polly;
using Polly.Extensions.Http;

namespace SmartphoneMonitor
{
    public partial class App : Application
    {
        private IHost? _host;
        private System.Diagnostics.Process? _pythonProcess;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // configure Serilog before building host
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "monitor.log"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Автозапуск Python-сервиса
            StartPythonService();

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // Shared in-memory channels
                    var listingChannel = Channel.CreateUnbounded<Listing>();
                    var hotDealChannel = Channel.CreateUnbounded<HotDeal>();
                    services.AddSingleton(listingChannel);
                    services.AddSingleton(hotDealChannel);

                    // Core services
                    services.AddSingleton<WebScraperService>();
                    // Http clients with Polly policies
                    var retryPolicy = HttpPolicyExtensions.HandleTransientHttpError().WaitAndRetryAsync(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) });
                    var circuitBreaker = HttpPolicyExtensions.HandleTransientHttpError().CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));

                    services.AddHttpClient("telegram", client => {
                        client.BaseAddress = new Uri("https://api.telegram.org");
                        client.Timeout = TimeSpan.FromSeconds(15);
                    })
                    .AddPolicyHandler(retryPolicy)
                    .AddPolicyHandler(circuitBreaker);

                    services.AddHttpClient("scraper", client => {
                        client.Timeout = TimeSpan.FromSeconds(30);
                    })
                    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError().RetryAsync(2));
                    services.AddSingleton<DatabaseService>();
                    services.AddSingleton<MetricsRepository>();
                    services.AddSingleton<MetricsService>();
                    services.AddHostedService(sp => sp.GetRequiredService<MetricsService>());
                    services.AddSingleton<DataAnalysisService>();
                    services.AddSingleton<TelegramNotificationService>();
                    services.AddHostedService<RetryFailedNotificationsAgent>();
                    services.AddSingleton<RetailPromoService>();

                    // Hosted agents
                    services.AddHostedService(sp => new ScraperAgent(sp.GetRequiredService<WebScraperService>(), listingChannel, sp.GetService<DatabaseService>()));
                    services.AddHostedService(sp => new AnalysisAgent(listingChannel, hotDealChannel, sp.GetRequiredService<DataAnalysisService>(), sp.GetService<DatabaseService>()));
                    services.AddHostedService(sp => new NotificationAgent(hotDealChannel, sp.GetService<TelegramNotificationService>(), sp.GetService<DatabaseService>()));
                    services.AddHostedService<PromoSyncAgent>();
                    services.AddHostedService<BrandSpikeAgent>();
                })
                .Build();

            await _host.StartAsync();
        }

        private void StartPythonService()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string pythonPath = "python";
                string userPython = $@"C:\Users\{Environment.UserName}\AppData\Local\Programs\Python\Python312\python.exe";
                if (File.Exists(userPython))
                {
                    pythonPath = userPython;
                }

                // Поиск скрипта main.py
                string scriptPath = Path.Combine(baseDir, "scripts", "matching_service", "main.py");
                if (!File.Exists(scriptPath))
                {
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "matching_service", "main.py");
                }

                if (File.Exists(scriptPath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(scriptPath)
                    };

                    _pythonProcess = System.Diagnostics.Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Не удалось автоматически запустить Python-сервис сопоставления.");
            }
        }

        private void StopPythonService()
        {
            try
            {
                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill(true); // Убиваем процесс вместе со всеми дочерними процессами
                    _pythonProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ошибка при остановке Python-сервиса.");
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            StopPythonService();

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
            base.OnExit(e);
        }
    }
}
