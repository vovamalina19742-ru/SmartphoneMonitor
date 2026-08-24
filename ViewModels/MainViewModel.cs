using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Media;
using Newtonsoft.Json;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;
using SmartphoneMonitor.Views;

namespace SmartphoneMonitor.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WebScraperService _scraperService;
        private readonly DataAnalysisService _analysisService;
        private readonly DatabaseService _databaseService;
        private readonly ExchangeRateService _exchangeRateService;
        private readonly TelegramNotificationService _telegramService;
        private readonly MatchingClientService _matchingClientService;
        private readonly DispatcherTimer _autoMonitorTimer;
        private readonly Dispatcher _dispatcher;
        private readonly Random _random = new Random();
        private readonly ReaderWriterLockSlim _resultLock = new ReaderWriterLockSlim();

        private List<Listing>? _allScrapedListings;
        private CancellationTokenSource? _cts;
        private bool _isAutoRunInProgress;
        private bool _disposed;

        private bool _isBusy;
        private string _status = "Готов к работе";
        private int _maxPages = 5;
        private bool _fetchDetails = true;
        private bool _isAutoMonitoring;
        private string _selectedIntervalString = "5 минут";

        private string _geminiApiKey = string.Empty;
        private string _telegramToken = string.Empty;
        private string _telegramChatId = string.Empty;
        private bool _telegramEnabled;
        private bool _soundAlertEnabled = true;

        private AnalysisResult _result = new AnalysisResult();

        public AnalysisResult Result
        {
            get
            {
                _resultLock.EnterReadLock();
                try
                {
                    return _result;
                }
                finally
                {
                    _resultLock.ExitReadLock();
                }
            }
            set
            {
                _resultLock.EnterWriteLock();
                try
                {
                    _result = value;
                }
                finally
                {
                    _resultLock.ExitWriteLock();
                }
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasViewsData));
                OnPropertyChanged(nameof(HasHotDeals));
                OnPropertyChanged(nameof(HasChronicSellers));
                UpdateBrandFilterOptions();
                RefreshFilteredLists();
            }
        }

        private bool _sortByViews;
        private int _minViews;

        private string _selectedBrandFilter = "Все бренды";
        private string _searchKeyword = string.Empty;
        private string _selectedSortOption = "По умолчанию";

        private string _newBlacklistNumber = string.Empty;
        private string _newBlacklistReason = string.Empty;
        private string _newBlacklistLogin = string.Empty;
        private string _newBlacklistLoginReason = string.Empty;
        private double _progress;
        private string _summaryText = "Ожидание запуска...";

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsReady));
                    StartCommand.Raise();
                    CancelCommand.Raise();
                }
            }
        }

        public bool IsReady => !IsBusy;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public int MaxPages
        {
            get => _maxPages;
            set
            {
                if (SetProperty(ref _maxPages, value))
                {
                    OnPropertyChanged(nameof(EstimatedListings));
                    _databaseService.SaveSetting("MaxPages", value.ToString());
                }
            }
        }

        public int EstimatedListings => MaxPages == 999 ? 3500 : MaxPages * 78;

        public bool FetchDetails
        {
            get => _fetchDetails;
            set
            {
                if (SetProperty(ref _fetchDetails, value))
                {
                    _databaseService.SaveSetting("FetchDetails", value.ToString());
                }
            }
        }

        public bool IsAutoMonitoring
        {
            get => _isAutoMonitoring;
            set
            {
                if (SetProperty(ref _isAutoMonitoring, value))
                {
                    _databaseService.SaveSetting("IsAutoMonitoring", value.ToString());
                    UpdateAutoMonitorState();
                }
            }
        }

        public string SelectedIntervalString
        {
            get => _selectedIntervalString;
            set
            {
                if (SetProperty(ref _selectedIntervalString, value))
                {
                    _databaseService.SaveSetting("SelectedIntervalString", value);
                    UpdateAutoMonitorState();
                }
            }
        }

        public string GeminiApiKey
        {
            get => _geminiApiKey;
            set => SetProperty(ref _geminiApiKey, value);
        }

        public string TelegramToken
        {
            get => _telegramToken;
            set => SetProperty(ref _telegramToken, value);
        }

        public string TelegramChatId
        {
            get => _telegramChatId;
            set => SetProperty(ref _telegramChatId, value);
        }

        public bool TelegramEnabled
        {
            get => _telegramEnabled;
            set => SetProperty(ref _telegramEnabled, value);
        }

        public bool SoundAlertEnabled
        {
            get => _soundAlertEnabled;
            set => SetProperty(ref _soundAlertEnabled, value);
        }

        public ObservableCollection<string> IntervalOptions { get; } = new ObservableCollection<string>
        {
            "5 минут", "10 минут", "15 минут", "30 минут", "60 минут"
        };

        public bool HasResult => Result != null && Result.TotalListings > 0;
        public bool HasViewsData => HasResult && Result.AllPrivateListings.Any(l => l.Views > 0);
        public bool HasHotDeals => HasResult && Result.HotDeals.Count > 0;
        public bool HasChronicSellers => HasResult && Result.ChronicSellers.Count > 0;

        public ObservableCollection<ViewsBucket> ViewsDistribution { get; } = new ObservableCollection<ViewsBucket>();

        public bool SortByViews
        {
            get => _sortByViews;
            set
            {
                if (SetProperty(ref _sortByViews, value))
                {
                    RefreshFilteredHotDeals();
                }
            }
        }

        public int MinViews
        {
            get => _minViews;
            set
            {
                if (SetProperty(ref _minViews, value))
                {
                    RefreshFilteredHotDeals();
                }
            }
        }

        private readonly ObservableCollection<HotDeal> _filteredHotDeals = new ObservableCollection<HotDeal>();
        public ObservableCollection<HotDeal> FilteredHotDeals => _filteredHotDeals;

        public int FilteredCount => FilteredHotDeals.Count;

        public ObservableCollection<string> BrandFilterOptions { get; } = new ObservableCollection<string> { "Все бренды" };

        public string SelectedBrandFilter
        {
            get => _selectedBrandFilter;
            set
            {
                if (SetProperty(ref _selectedBrandFilter, value))
                {
                    RefreshFilteredListings();
                }
            }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                if (SetProperty(ref _searchKeyword, value))
                {
                    RefreshFilteredListings();
                }
            }
        }

        public ObservableCollection<string> SortOptions { get; } = new ObservableCollection<string>
        {
            "По умолчанию",
            "Сначала дешевые",
            "Сначала дорогие",
            "Сначала новые объявления",
            "Сначала просматриваемые"
        };

        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (SetProperty(ref _selectedSortOption, value))
                {
                    RefreshFilteredListings();
                }
            }
        }

        private readonly ObservableCollection<Listing> _filteredListingsList = new ObservableCollection<Listing>();
        public ObservableCollection<Listing> FilteredListingsList => _filteredListingsList;

        public string NewBlacklistNumber
        {
            get => _newBlacklistNumber;
            set => SetProperty(ref _newBlacklistNumber, value);
        }

        public string NewBlacklistReason
        {
            get => _newBlacklistReason;
            set => SetProperty(ref _newBlacklistReason, value);
        }

        public string NewBlacklistLogin
        {
            get => _newBlacklistLogin;
            set => SetProperty(ref _newBlacklistLogin, value);
        }

        public string NewBlacklistLoginReason
        {
            get => _newBlacklistLoginReason;
            set => SetProperty(ref _newBlacklistLoginReason, value);
        }

        public ObservableCollection<BlacklistEntry> Blacklist { get; } = new ObservableCollection<BlacklistEntry>();
        public ObservableCollection<BlacklistLoginEntry> BlacklistLogins { get; } = new ObservableCollection<BlacklistLoginEntry>();
        public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        // Commands
        public RelayCommand SetPagesCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand OpenUrlCommand { get; }
        public RelayCommand AddToBlacklistCommand { get; }
        public RelayCommand RemoveBlacklistCommand { get; }
        public RelayCommand AddBlacklistCommand { get; }
        public RelayCommand SetMinViewsCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand TestTelegramCommand { get; }
        public RelayCommand UpdateRetailPricesCommand { get; }
        public RelayCommand OpenPriceAnalysisCommand { get; }
        public RelayCommand AddBlacklistLoginCommand { get; }
        public RelayCommand RemoveBlacklistLoginCommand { get; }
        public RelayCommand RunVisionAnalysisCommand { get; }
        public RelayCommand RefreshDemandArbitrageCommand { get; }
        public RelayCommand BlacklistDemandAuthorCommand { get; }

        public ObservableCollection<DemandArbitrageDeal> DemandArbitrageDeals { get; } = new ObservableCollection<DemandArbitrageDeal>();

        public MainViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _scraperService = new WebScraperService();
            _analysisService = new DataAnalysisService();
            _databaseService = new DatabaseService();
            _exchangeRateService = new ExchangeRateService();
            _telegramService = new TelegramNotificationService();
            _matchingClientService = new MatchingClientService();
            _telegramService.BlacklistRequested += OnBlacklistRequested;
            _telegramService.BlacklistLoginRequested += OnBlacklistLoginRequested;

            _autoMonitorTimer = new DispatcherTimer();
            _autoMonitorTimer.Tick += OnAutoMonitorTick;

            // Initialize commands
            SetPagesCommand = new RelayCommand(p =>
            {
                if (p != null && int.TryParse(p.ToString(), out int pages))
                {
                    MaxPages = pages;
                }
            });

            StartCommand = new RelayCommand(async p => await RunAnalysisAsync(), p => !IsBusy);

            OpenUrlCommand = new RelayCommand(p =>
            {
                if (p != null)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = p.ToString()!,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ Ошибка открытия ссылки: {ex.Message}");
                    }
                }
            });

            OpenPriceAnalysisCommand = new RelayCommand(p =>
            {
                if (p is HotDeal deal)
                {
                    try
                    {
                        var history = _databaseService.GetPriceHistoryForBrandAndModel(deal.Brand, deal.Title, deal.StorageGB);
                        var win = new Views.PriceAnalysisWindow(deal.Brand, deal.Title, deal.StorageGB, history);
                        win.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"❌ Ошибка открытия аналитики цен: {ex.Message}");
                    }
                }
            });

            AddToBlacklistCommand = new RelayCommand(p =>
            {
                if (p is ChronicSeller seller)
                {
                    AddPhoneToBlacklist(seller.PhoneNumber, seller.Reason);
                }
            });

            RemoveBlacklistCommand = new RelayCommand(p =>
            {
                if (p is BlacklistEntry entry)
                {
                    RemovePhoneFromBlacklist(entry.PhoneNumber);
                }
            });

            AddBlacklistCommand = new RelayCommand(p =>
            {
                if (!string.IsNullOrWhiteSpace(NewBlacklistNumber))
                {
                    AddPhoneToBlacklist(NewBlacklistNumber.Trim(), NewBlacklistReason.Trim());
                    NewBlacklistNumber = string.Empty;
                    NewBlacklistReason = string.Empty;
                }
            });

            AddBlacklistLoginCommand = new RelayCommand(p =>
            {
                if (!string.IsNullOrWhiteSpace(NewBlacklistLogin))
                {
                    AddLoginToBlacklist(NewBlacklistLogin.Trim(), NewBlacklistLoginReason.Trim());
                    NewBlacklistLogin = string.Empty;
                    NewBlacklistLoginReason = string.Empty;
                }
            });

            RemoveBlacklistLoginCommand = new RelayCommand(p =>
            {
                if (p is BlacklistLoginEntry entry)
                {
                    RemoveLoginFromBlacklist(entry.Login);
                }
            });

            SetMinViewsCommand = new RelayCommand(p =>
            {
                if (p != null && int.TryParse(p.ToString(), out int min))
                {
                    MinViews = min;
                }
            });

            SaveSettingsCommand = new RelayCommand(async p =>
            {
                try
                {
                    _databaseService.SaveSetting("TelegramToken", TelegramToken);
                    _databaseService.SaveSetting("TelegramChatId", TelegramChatId);
                    _databaseService.SaveSetting("TelegramEnabled", TelegramEnabled.ToString());
                    _databaseService.SaveSetting("SoundAlertEnabled", SoundAlertEnabled.ToString());
                    _databaseService.SaveSetting("GeminiApiKey", GeminiApiKey);
                    LogMessage("💾 Настройки сохранены!");

                    if (TelegramEnabled && !string.IsNullOrEmpty(TelegramToken) && !string.IsNullOrEmpty(TelegramChatId))
                    {
                        LogMessage("🧪 Проверка Telegram-токена...");
                        bool ok = await _telegramService.SendTestMessageAsync(TelegramToken, TelegramChatId);
                        if (ok)
                        {
                            LogMessage("✅ Telegram-бот работает! Отправлено тестовое сообщение.");
                            _telegramService.StartPolling(TelegramToken, TelegramChatId);
                        }
                        else
                        {
                            LogMessage("❌ Ошибка: не удалось отправить тестовое сообщение. Проверьте Token и Chat ID.");
                            LogMessage("💡 Убедитесь, что вы написали боту /start и у бота нет ограничений.");
                        }
                    }
                    else
                    {
                        _telegramService.StopPolling();
                        if (TelegramEnabled)
                            LogMessage("⚠️ Telegram включен, но Token или Chat ID не указаны.");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ Ошибка сохранения настроек: {ex.Message}");
                }
            });

            TestTelegramCommand = new RelayCommand(async p =>
            {
                LogMessage("🧪 Проверка отправки тестового сообщения в Telegram...");
                bool ok = await _telegramService.SendTestMessageAsync(TelegramToken, TelegramChatId);
                if (ok)
                {
                    LogMessage("✅ Тестовое сообщение успешно отправлено! Проверьте ваш Telegram.");
                }
                else
                {
                    LogMessage("❌ Ошибка отправки тестового сообщения. Проверьте Token и Chat ID.");
                }
            });

            UpdateRetailPricesCommand = new RelayCommand(async p => await RunUpdateRetailPricesAsync(), p => !IsBusy);

            RunVisionAnalysisCommand = new RelayCommand(async p =>
            {
                if (p is HotDeal deal)
                {
                    await RunVisionAnalysisAsync(deal);
                }
            });

            // Load saved settings
            LoadSavedSettings();

            if (TelegramEnabled && !string.IsNullOrEmpty(TelegramToken))
            {
                _telegramService.StartPolling(TelegramToken, TelegramChatId);
            }

            CancelCommand = new RelayCommand(p => CancelScan(), p => IsBusy);

            // Clean database cache older than 30 days
            Task.Run(() =>
            {
                try
                {
                    _databaseService.CleanPriceHistory(30);
                }
                catch { }
            });

            // Fetch and update EUR rate asynchronously without blocking
            Task.Run(async () =>
            {
                decimal rate = await _exchangeRateService.GetEurToMdlRateAsync(Constants.EurToMdl);
                Constants.EurToMdl = rate;
                _databaseService.SaveSetting("EurToMdl", rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
                LogMessage($"🏦 Загружен курс НБМ: 1 EUR = {rate:F4} MDL");
            });

            RefreshDemandArbitrageCommand = new RelayCommand(async _ => await RunDemandArbitrageAsync());
            BlacklistDemandAuthorCommand = new RelayCommand(param =>
            {
                if (param is DemandArbitrageDeal deal)
                {
                    if (!string.IsNullOrEmpty(deal.DemandAuthor))
                    {
                        _databaseService.AddBlacklistLogin(deal.DemandAuthor, "Черный список Арбитража");
                        AddLoginToBlacklist(deal.DemandAuthor, "Черный список Арбитража");
                    }
                    if (!string.IsNullOrEmpty(deal.DemandPhone))
                    {
                        _databaseService.AddBlacklist(deal.DemandPhone, "Черный список Арбитража");
                        AddPhoneToBlacklist(deal.DemandPhone, "Черный список Арбитража");
                    }
                    _dispatcher.Invoke(() => DemandArbitrageDeals.Remove(deal));
                }
            });
        }

        private void OnBlacklistRequested(string phone, string reason)
        {
            _dispatcher.Invoke(() => AddPhoneToBlacklist(phone, reason));
        }

        private void OnBlacklistLoginRequested(string login, string reason)
        {
            _dispatcher.Invoke(() => AddLoginToBlacklist(login, reason));
        }

        private void CancelScan()
        {
            var cts = _cts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                try
                {
                    cts.Cancel();
                    LogMessage("⏹️ Сканирование отменено пользователем.");
                    Status = "Отмена...";
                }
                catch (ObjectDisposedException) { }
                catch (AggregateException) { }
            }
        }

        private void LoadSavedSettings()
        {
            try
            {
                // Blacklist
                var savedBlacklist = _databaseService.GetBlacklist();
                foreach (var entry in savedBlacklist)
                {
                    Blacklist.Add(entry);
                }

                var savedLogins = _databaseService.GetBlacklistLogins();
                foreach (var entry in savedLogins)
                {
                    BlacklistLogins.Add(entry);
                }

                // EUR Rate
                if (decimal.TryParse(_databaseService.GetSetting("EurToMdl", "19.5"), out var savedRate))
                {
                    Constants.EurToMdl = savedRate;
                }

                // Settings
                if (int.TryParse(_databaseService.GetSetting("MaxPages", "5"), out int pages))
                {
                    _maxPages = pages;
                }
                _fetchDetails = bool.TryParse(_databaseService.GetSetting("FetchDetails", "true"), out bool details) && details;
                _isAutoMonitoring = bool.TryParse(_databaseService.GetSetting("IsAutoMonitoring", "false"), out bool auto) && auto;
                _selectedIntervalString = _databaseService.GetSetting("SelectedIntervalString", "5 минут");

                _telegramToken = _databaseService.GetSetting("TelegramToken", string.Empty);
                _telegramChatId = _databaseService.GetSetting("TelegramChatId", string.Empty);
                _telegramEnabled = bool.TryParse(_databaseService.GetSetting("TelegramEnabled", "false"), out bool tgEnabled) && tgEnabled;
                _soundAlertEnabled = bool.TryParse(_databaseService.GetSetting("SoundAlertEnabled", "true"), out bool sndEnabled) && sndEnabled;
                _geminiApiKey = _databaseService.GetSetting("GeminiApiKey", string.Empty);

                OnPropertyChanged(nameof(TelegramToken));
                OnPropertyChanged(nameof(TelegramChatId));
                OnPropertyChanged(nameof(TelegramEnabled));
                OnPropertyChanged(nameof(SoundAlertEnabled));
                OnPropertyChanged(nameof(GeminiApiKey));

                UpdateAutoMonitorState();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка загрузки настроек: {ex.Message}");
            }
        }

        private void UpdateAutoMonitorState()
        {
            _autoMonitorTimer.Stop();
            if (IsAutoMonitoring)
            {
                _autoMonitorTimer.Interval = GetAutoMonitorInterval();
                _autoMonitorTimer.Start();
                LogMessage($"⏱️ Авто-мониторинг запущен с интервал {_selectedIntervalString}");
            }
            else
            {
                var cts = _cts;
                if (_isAutoRunInProgress && cts != null && !cts.IsCancellationRequested)
                {
                    try
                    {
                        cts.Cancel();
                        LogMessage("⏹️ Отключение авто-мониторинга: текущий цикл отменяется.");
                    }
                    catch (ObjectDisposedException) { }
                }
                LogMessage("⏱️ Авто-мониторинг отключен");
            }
        }

        private TimeSpan GetAutoMonitorInterval()
        {
            int minutes = 5;
            if (SelectedIntervalString.Contains("10")) minutes = 10;
            else if (SelectedIntervalString.Contains("15")) minutes = 15;
            else if (SelectedIntervalString.Contains("30")) minutes = 30;
            else if (SelectedIntervalString.Contains("60")) minutes = 60;
            return TimeSpan.FromMinutes(minutes);
        }

        private async void OnAutoMonitorTick(object? sender, EventArgs e)
        {
            try
            {
                if (IsBusy)
                {
                    LogMessage("⏱️ Очередной цикл авто-мониторинга пропущен (система занята).");
                    return;
                }
                if (!IsAutoMonitoring)
                {
                    return;
                }
                LogMessage("⏱️ Запуск фонового авто-мониторинга...");
                await RunAnalysisAsync(isAutoRun: true);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка авто-мониторинга: {ex.Message}");
            }
        }

        private async Task RunAnalysisAsync(bool isAutoRun = false)
        {
            // Proactively collect garbage on start to keep memory utilization minimal on weak CPUs
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch { }

            IsBusy = true;
            Progress = 0;
            Status = isAutoRun ? "Авто-мониторинг..." : "Сбор данных...";
            SummaryText = "Запуск сбора объявлений...";
            LogMessage(isAutoRun ? "🚀 Старт фонового сканирования..." : "🚀 Ручной запуск сканирования...");

            var progressReporter = new Progress<string>(msg =>
            {
                _dispatcher.Invoke(() =>
                {
                    Status = msg;
                    LogMessage(msg);
                });
            });

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _isAutoRunInProgress = isAutoRun;
            var token = _cts.Token;

            try
            {
                // Fetch price history before run to identify new deals
                var preRunHistory = _databaseService.GetPriceHistory();

                var rawListings = await Task.Run(() => _scraperService.ScrapeSmartphonesAsync(MaxPages, Constants.EurToMdl, progressReporter, token), token);

                token.ThrowIfCancellationRequested();
                _allScrapedListings = rawListings;

                if (rawListings.Count == 0)
                {
                    SummaryText = "Объявлений не найдено.";
                    IsBusy = false;
                    return;
                }

                // If details requested, download seller details in parallel (max 3 concurrent requests)
                if (FetchDetails)
                {
                    int total = rawListings.Count;
                    int processed = 0;
                    using (var semaphore = new SemaphoreSlim(2))
                    {
                        var tasks = rawListings.Select(async l =>
                        {
                            await semaphore.WaitAsync(token);
                            try
                            {
                                token.ThrowIfCancellationRequested();
                                // Add a human-like delay to prevent concurrent burst requests (2.5 to 4.5 seconds)
                                await Task.Delay(_random.Next(2500, 4500), token);
                                var details = await _scraperService.FetchPhoneAsync(l.Url, token);
                                l.PhoneNumber = details.phone;
                                l.SellerName = details.seller;
                                l.Description = details.description;
                                l.ImageUrls = details.images;
                                if (details.views > 0)
                                {
                                    l.Views = details.views;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch { }
                            finally
                            {
                                semaphore.Release();
                                int current = Interlocked.Increment(ref processed);
                                _dispatcher.Invoke(() =>
                                {
                                    Progress = (double)current * 100.0 / total;
                                    Status = $"Сбор контактов: {current}/{total}...";
                                });
                            }
                        }).ToArray();

                        await Task.WhenAll(tasks);
                    }
                }

                token.ThrowIfCancellationRequested();

                // Save new price history
                _databaseService.SavePriceHistory(rawListings);

                // Run data analysis
                Status = "Анализ данных...";
                var blacklistPhones = Blacklist.Select(b => b.PhoneNumber).ToList();
                var blacklistLogins = BlacklistLogins.Select(b => b.Login).ToList();
                var analysisResult = await Task.Run(() => _analysisService.Analyze(rawListings, blacklistPhones, blacklistLogins, preRunHistory), token);

                Result = analysisResult;
                await RunDemandArbitrageAsync();

                // Export Apple & Xiaomi listings to CSV history for Google Drive Sync
                try
                {
                    await Task.Run(() => _databaseService.ExportListingsToCsv(analysisResult.AllPrivateListings), token);
                }
                catch { }

                // Build views distribution chart buckets
                UpdateViewsDistribution(analysisResult.AllPrivateListings);

                // Detect new hot deals (not present in database history before this run)
                var newDeals = analysisResult.HotDeals.Where(hd => !preRunHistory.ContainsKey(hd.Url)).ToList();
                if (newDeals.Count > 0)
                {
                    _dispatcher.Invoke(() =>
                    {
                        if (newDeals.Count == 1)
                        {
                            var toast = new NotificationWindow(newDeals[0]);
                            toast.Show();
                        }
                        else
                        {
                            var toast = new NotificationWindow(null, true, newDeals.Count);
                            toast.Show();
                        }
                    });

                    if (SoundAlertEnabled)
                    {
                        try
                        {
                            SystemSounds.Asterisk.Play();
                        }
                        catch { }
                    }

                    if (TelegramEnabled)
                    {
                        if (string.IsNullOrEmpty(TelegramToken) || string.IsNullOrEmpty(TelegramChatId))
                        {
                            LogMessage("⚠️ [Telegram] Уведомления включены, но TelegramToken или TelegramChatId не заполнены в Настройках!");
                        }
                        else
                        {
                            LogMessage($"📢 [Telegram] Отправка {newDeals.Count} новых горячих сделок в Telegram...");
                            foreach (var deal in newDeals)
                            {
                                var d = deal;
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        bool sent = await _telegramService.SendHotDealNotificationAsync(TelegramToken, TelegramChatId, d);
                                        _dispatcher.Invoke(() =>
                                        {
                                            if (sent)
                                                LogMessage($"✅ [Telegram] Сделка '{d.Title}' успешно отправлена в Telegram!");
                                            else
                                                LogMessage($"❌ [Telegram] Ошибка отправки сделки '{d.Title}'. Проверьте Token и Chat ID.");
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        _dispatcher.Invoke(() => LogMessage($"❌ [Telegram] Исключение при отправке: {ex.Message}"));
                                    }
                                });
                            }
                        }
                    }
                }

                SummaryText = $"Анализ завершен за {analysisResult.AnalysisDuration.TotalSeconds:F1} сек. Найдено: {analysisResult.TotalListings} всего, {analysisResult.PrivateListings} частных, {analysisResult.ChronicSellers.Count} перекупщиков.";
                LogMessage("✅ Анализ успешно завершен.");
            }
            catch (OperationCanceledException)
            {
                LogMessage("⏹️ Сканирование было отменено.");
                SummaryText = "Сканирование отменено.";
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Критическая ошибка при анализе: {ex.Message}");
                SummaryText = "Анализ завершился с ошибкой.";
            }
            finally
            {
                _isAutoRunInProgress = false;
                IsBusy = false;
                Progress = 0;
                Status = "Готов к работе";
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void UpdateViewsDistribution(List<Listing> privateListings)
        {
            ViewsDistribution.Clear();
            var viewed = privateListings.Where(l => l.Views > 0).ToList();
            if (viewed.Count == 0) return;

            int maxViews = viewed.Max(l => l.Views);
            int step = maxViews / 5;
            if (step < 10) step = 10;

            for (int i = 0; i < 5; i++)
            {
                int min = i * step;
                int max = (i == 4) ? int.MaxValue : (i + 1) * step - 1;
                string label = (i == 4) ? $"{min}+ 👁" : $"{min}-{max} 👁";
                int count = viewed.Count(l => l.Views >= min && l.Views <= max);

                ViewsDistribution.Add(new ViewsBucket
                {
                    Label = label,
                    Count = count,
                    Fraction = (double)count / viewed.Count
                });
            }
        }

        private void UpdateBrandFilterOptions()
        {
            string selected = SelectedBrandFilter;
            BrandFilterOptions.Clear();
            BrandFilterOptions.Add("Все бренды");

            if (Result?.BrandStats != null)
            {
                foreach (var stat in Result.BrandStats)
                {
                    BrandFilterOptions.Add(stat.Brand);
                }
            }

            if (BrandFilterOptions.Contains(selected))
            {
                SelectedBrandFilter = selected;
            }
            else
            {
                SelectedBrandFilter = "Все бренды";
            }
        }

        private void RefreshFilteredLists()
        {
            RefreshFilteredHotDeals();
            RefreshFilteredListings();
        }

        private void RefreshFilteredHotDeals()
        {
            _dispatcher.Invoke(() =>
            {
                FilteredHotDeals.Clear();
                if (Result?.HotDeals == null)
                {
                    OnPropertyChanged(nameof(FilteredCount));
                    return;
                }

                var query = Result.HotDeals.Where(hd => hd.Views >= MinViews);

                if (SortByViews)
                {
                    query = query.OrderByDescending(hd => hd.Views);
                }
                else
                {
                    query = query.OrderByDescending(hd => hd.RecommendationScore);
                }

                foreach (var deal in query)
                {
                    FilteredHotDeals.Add(deal);
                }
                OnPropertyChanged(nameof(FilteredCount));
            });
        }

        private void RefreshFilteredListings()
        {
            _dispatcher.Invoke(() =>
            {
                FilteredListingsList.Clear();
                if (Result?.AllPrivateListings == null) return;

                var query = Result.AllPrivateListings.AsEnumerable();

                if (SelectedBrandFilter != "Все бренды")
                {
                    query = query.Where(l => l.Brand == SelectedBrandFilter);
                }

                if (!string.IsNullOrWhiteSpace(SearchKeyword))
                {
                    string k = SearchKeyword.Trim().ToLowerInvariant();
                    query = query.Where(l => l.Title.ToLowerInvariant().Contains(k) || l.Description.ToLowerInvariant().Contains(k) || l.Model.ToLowerInvariant().Contains(k));
                }

                query = SelectedSortOption switch
                {
                    "Сначала дешевые" => query.OrderBy(l => l.PriceValue),
                    "Сначала дорогие" => query.OrderByDescending(l => l.PriceValue),
                    "Сначала новые объявления" => query.OrderBy(l => l.DaysOld >= 0 ? l.DaysOld : 999).ThenByDescending(l => l.PostedDate),
                    "Сначала просматриваемые" => query.OrderByDescending(l => l.Views),
                    _ => query.OrderByDescending(l => l.PostedDate)
                };

                foreach (var item in query)
                {
                    FilteredListingsList.Add(item);
                }
            });
        }

        private void AddPhoneToBlacklist(string phone, string reason)
        {
            try
            {
                string norm = ListingClassifier.NormalizePhone(phone);
                if (string.IsNullOrEmpty(norm)) return;

                _dispatcher.Invoke(() =>
                {
                    if (Blacklist.Any(b => ListingClassifier.NormalizePhone(b.PhoneNumber) == norm))
                    {
                        LogMessage($"⚠️ Номер {phone} уже в черном списке.");
                        return;
                    }

                    _databaseService.AddBlacklist(phone, reason);
                    Blacklist.Add(new BlacklistEntry
                    {
                        PhoneNumber = phone,
                        Reason = reason,
                        DateAdded = DateTime.Now
                    });

                    LogMessage($"🚫 Добавлен в ЧС: {phone} ({reason})");
                    OnPropertyChanged(nameof(Blacklist));
                });

                if (Result != null && _allScrapedListings != null)
                {
                    _ = RefreshAnalysisWithCurrentBlacklistsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка добавления в ЧС: {ex.Message}");
            }
        }

        private void RemovePhoneFromBlacklist(string phone)
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    _databaseService.RemoveBlacklist(phone);
                    var entry = Blacklist.FirstOrDefault(b => b.PhoneNumber == phone);
                    if (entry != null)
                    {
                        Blacklist.Remove(entry);
                    }

                    LogMessage($"✅ Удален из ЧС: {phone}");
                    OnPropertyChanged(nameof(Blacklist));
                });

                if (Result != null && _allScrapedListings != null)
                {
                    _ = RefreshAnalysisWithCurrentBlacklistsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка удаления из ЧС: {ex.Message}");
            }
        }

        private void AddLoginToBlacklist(string login, string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(login)) return;

                _dispatcher.Invoke(() =>
                {
                    if (BlacklistLogins.Any(b => b.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
                    {
                        LogMessage($"⚠️ Логин {login} уже в черном списке.");
                        return;
                    }

                    _databaseService.AddBlacklistLogin(login, reason);
                    BlacklistLogins.Add(new BlacklistLoginEntry
                    {
                        Login = login,
                        Reason = reason,
                        DateAdded = DateTime.Now
                    });

                    LogMessage($"🚫 Добавлен в ЧС логинов: {login} ({reason})");
                    OnPropertyChanged(nameof(BlacklistLogins));
                });

                if (Result != null && _allScrapedListings != null)
                {
                    _ = RefreshAnalysisWithCurrentBlacklistsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка добавления логина в ЧС: {ex.Message}");
            }
        }

        private void RemoveLoginFromBlacklist(string login)
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    _databaseService.RemoveBlacklistLogin(login);
                    var entry = BlacklistLogins.FirstOrDefault(b => b.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        BlacklistLogins.Remove(entry);
                    }

                    LogMessage($"✅ Удален из ЧС логинов: {login}");
                    OnPropertyChanged(nameof(BlacklistLogins));
                });

                if (Result != null && _allScrapedListings != null)
                {
                    _ = RefreshAnalysisWithCurrentBlacklistsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка удаления логина из ЧС: {ex.Message}");
            }
        }

        private async Task RefreshAnalysisWithCurrentBlacklistsAsync()
        {
            if (_allScrapedListings == null)
            {
                return;
            }

            await Task.Run(() =>
            {
                var blacklistPhones = _dispatcher.Invoke(() => Blacklist.Select(b => b.PhoneNumber).ToList());
                var blacklistLogins = _dispatcher.Invoke(() => BlacklistLogins.Select(b => b.Login).ToList());
                var analysisResult = _analysisService.Analyze(_allScrapedListings, blacklistPhones, blacklistLogins, _databaseService.GetPriceHistory());
                _dispatcher.Invoke(() =>
                {
                    Result = analysisResult;
                    UpdateViewsDistribution(analysisResult.AllPrivateListings);
                });
            });
        }

        private void LogMessage(string message)
        {
            _dispatcher.Invoke(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                Log.Add($"[{time}] {message}");
                if (Log.Count > 300)
                {
                    Log.RemoveAt(0);
                }
            });
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private async Task NotifyTopRetailDiscountsAsync()
        {
            if (!TelegramEnabled || string.IsNullOrEmpty(TelegramToken) || string.IsNullOrEmpty(TelegramChatId))
            {
                return;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string jsonPath = Path.Combine(baseDir, "reports", "retail_promo_prices.json");
                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "reports", "retail_promo_prices.json");
                }
                if (!File.Exists(jsonPath)) return;

                string jsonContent = await File.ReadAllTextAsync(jsonPath);
                var allPromos = JsonConvert.DeserializeObject<List<RetailPromoPrice>>(jsonContent);
                if (allPromos == null || allPromos.Count == 0) return;

                var topPromos = allPromos
                    .Where(p => p.Price <= 5000m && p.Discount >= 500m)
                    .OrderByDescending(p => p.Discount)
                    .Take(10)
                    .ToList();

                if (topPromos.Count == 0) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("🛍️ <b>ТОП-10 ГОРЯЧИХ СКИДОК В МАГАЗИНАХ!</b> 📱");
                sb.AppendLine($"<i>База успешно обновлена. Всего найдено акций: {allPromos.Count}</i>\n");

                int idx = 1;
                foreach (var p in topPromos)
                {
                    decimal percent = p.OldPrice > 0 ? (p.Discount / p.OldPrice) * 100m : 0m;
                    string shopEmoji = p.Shop.ToLower() switch
                    {
                        "orange" => "🍊",
                        "moldcell" => "🍇",
                        "darwin" => "🐒",
                        "enter" => "💻",
                        "maximum" => "🔥",
                        "bomba" => "💣",
                        _ => "🛒"
                    };

                    sb.AppendLine($"{idx}. {shopEmoji} <b>{p.Shop}:</b> <a href=\"{p.Url}\">{p.Brand} {p.Name}</a>");
                    sb.AppendLine($"   🏷️ <b>Цена:</b> {p.Price:F0} MDL <s>{p.OldPrice:F0} MDL</s> (<b>Скидка: -{p.Discount:F0} MDL</b> | -{percent:F0}%)");
                    sb.AppendLine();
                    idx++;
                }

                await _telegramService.SendTextMessageAsync(TelegramToken, TelegramChatId, sb.ToString());
                _dispatcher.Invoke(new Action(() => LogMessage($"📢 Отправлен отчет о лучших скидках в Telegram! (топ-{topPromos.Count})")));
            }
            catch (Exception ex)
            {
                _dispatcher.Invoke(new Action(() => LogMessage($"⚠️ Ошибка отправки скидок в Telegram: {ex.Message}")));
            }
        }

        private async Task RunUpdateRetailPricesAsync()
        {
            IsBusy = true;
            Status = "Сбор цен крупных магазинов...";
            LogMessage("🛍️ Запуск обновления цен Darwin и Enter...");

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidatePaths = new[]
                {
                    Path.Combine(baseDir, "scripts", "scrape_retail.js"),
                    Path.Combine(Directory.GetCurrentDirectory(), "scripts", "scrape_retail.js"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "scripts", "scrape_retail.js"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "scripts", "scrape_retail.js")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "scripts", "scrape_retail.js"))
                };

                string? scriptPath = candidatePaths.FirstOrDefault(File.Exists);

                if (string.IsNullOrEmpty(scriptPath))
                {
                    LogMessage("❌ Ошибка: скрипт scrape_retail.js не найден.");
                    return;
                }

                string nodePath = @"C:\Program Files\nodejs\node.exe";
                if (!File.Exists(nodePath))
                {
                    nodePath = "node";
                }

                await Task.Run(() =>
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = nodePath,
                        Arguments = $"\"{scriptPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            _dispatcher.Invoke(() => LogMessage("❌ Не удалось запустить процесс Node.js."));
                            return;
                        }

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            _dispatcher.Invoke(() => LogMessage("🛍️ Цены магазинов успешно обновлены!"));
                            _ = Task.Run(async () => await NotifyTopRetailDiscountsAsync());
                        }
                        else
                        {
                            _dispatcher.Invoke(() => LogMessage($"❌ Ошибка парсера магазинов:\n{error}"));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка запуска парсера магазинов: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                Status = "Готов к работе";
            }
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task RunVisionAnalysisAsync(HotDeal deal)
        {
            if (string.IsNullOrEmpty(GeminiApiKey))
            {
                LogMessage("❌ Ошибка: Введите ключ Gemini API в настройках!");
                return;
            }

            if (deal.ImageUrls == null || deal.ImageUrls.Count == 0)
            {
                // 1. Check SQLite database cache first
                var cachedImages = _databaseService.GetCachedImageUrls(deal.Url);
                if (cachedImages != null && cachedImages.Count > 0)
                {
                    deal.ImageUrls = cachedImages;
                    LogMessage($"[SQLite Cache] Извлечено {deal.ImageUrls.Count} фото из базы данных для '{deal.Title}'.");
                }
                else
                {
                    // 2. Fetch directly using multi-source listing image parser
                    LogMessage($"🔍 Загрузка фотографий для '{deal.Title}' по URL: {deal.Url}...");
                    try
                    {
                        IsBusy = true;
                        var fetchedImages = await _scraperService.FetchListingImagesAsync(deal.Url);
                        if (fetchedImages != null && fetchedImages.Count > 0)
                        {
                            deal.ImageUrls = fetchedImages;
                            // Save to SQLite so subsequent runs don't re-fetch
                            _databaseService.SavePriceHistory(new List<Listing>
                            {
                                new Listing
                                {
                                    Url = deal.Url,
                                    Title = deal.Title,
                                    Brand = deal.Brand,
                                    StorageGB = deal.StorageGB,
                                    PriceValue = deal.PriceValue,
                                    ImageUrls = deal.ImageUrls
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ Не удалось подгрузить фото с сайта: {ex.Message}");
                    }
                }
            }

            if (deal.ImageUrls == null || deal.ImageUrls.Count == 0)
            {
                LogMessage($"❌ [Parser] Объявление {deal.Title}: фото не найдено.");
                return;
            }

            LogMessage($"[Parser] Объявление {deal.Title}: найдено фото - {deal.ImageUrls.Count}");

            try
            {
                IsBusy = true;
                LogMessage($"👁️ Запуск Визуального ИИ-Арбитра для '{deal.Title}' ({deal.ImageUrls.Count} фото)...");
                
                var visionService = new SmartphoneMonitor.Services.AIVisionService();
                string result = await visionService.AnalyzeImagesAsync(deal.ImageUrls, GeminiApiKey);
                
                // Рассчитываем динамическую дельту визуальной оценки ИИ
                double visionDelta = 5.0; // Базовый бонус подтверждения наличия фото
                string lowerResult = result.ToLowerInvariant();
                
                if (lowerResult.Contains("идеальн") || lowerResult.Contains("без царапин") || lowerResult.Contains("отличн") || lowerResult.Contains("оригинал") || lowerResult.Contains("коробк") || lowerResult.Contains("комплект"))
                {
                    visionDelta = +15.0; // Бонус за идеальное состояние / комплект
                }
                else if (lowerResult.Contains("трещин") || lowerResult.Contains("разбит") || lowerResult.Contains("скол") || lowerResult.Contains("неродно") || lowerResult.Contains("выгоран") || lowerResult.Contains("дефект") || lowerResult.Contains("сорван"))
                {
                    visionDelta = -30.0; // Штраф за скрытый дефект или вскрытие
                }

                double oldScore = deal.ArbitrageScore;
                double newScore = Math.Max(0.0, Math.Min(100.0, oldScore + visionDelta));

                string visualInspectionText = $"\n\n👁️ ВИЗУАЛЬНЫЙ ИИ-АРБИТР ПО ФОТО (Корректировка: {(visionDelta >= 0 ? "+" : "")}{visionDelta:F0} баллов):\n{result}\n→ Итоговый ArbitrageScore: {newScore:F0}/100";
                
                _dispatcher.Invoke(() =>
                {
                    deal.AiReasoning += visualInspectionText;
                    deal.VisionDelta = visionDelta;
                    deal.IsVisionInspected = true;
                    deal.ArbitrageScore = newScore;
                    
                    // Пересортировываем коллекцию на лету, чтобы обновленный лот занял новое место
                    var sortedList = FilteredHotDeals.OrderByDescending(h => h.ArbitrageScore).ToList();
                    FilteredHotDeals.Clear();
                    foreach (var d in sortedList)
                    {
                        FilteredHotDeals.Add(d);
                    }
                    
                    OnPropertyChanged(nameof(FilteredHotDeals));
                });

                LogMessage($"✅ ИИ завершил осмотр! Индекс '{deal.Title}': {oldScore:F0} ➔ {newScore:F0}/100 ({(visionDelta >= 0 ? "+" : "")}{visionDelta:F0})");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка ИИ: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RunDemandArbitrageAsync()
        {
            try
            {
                var activeDemands = await Task.Run(() => _databaseService.GetActiveDemandListings());
                var currentSupplies = Result?.AllPrivateListings ?? new List<Listing>();

                if (activeDemands.Count > 0 && currentSupplies.Count > 0)
                {
                    var deals = await _matchingClientService.MatchArbitrageAsync(activeDemands, currentSupplies, 200m);
                    _dispatcher.Invoke(() =>
                    {
                        DemandArbitrageDeals.Clear();
                        foreach (var d in deals.OrderByDescending(x => x.PotentialProfit))
                        {
                            DemandArbitrageDeals.Add(d);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[MainViewModel] Ошибка при выполнении сопоставления арбитража спроса.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_telegramService != null)
            {
                _telegramService.BlacklistRequested -= OnBlacklistRequested;
                _telegramService.BlacklistLoginRequested -= OnBlacklistLoginRequested;
            }

            _autoMonitorTimer?.Stop();

            _cts?.Cancel();
            _cts?.Dispose();

            _resultLock?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}