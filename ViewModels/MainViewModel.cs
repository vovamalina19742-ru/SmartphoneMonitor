using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SmartphoneMonitor.Models;
using SmartphoneMonitor.Services;
using SmartphoneMonitor.Views;

namespace SmartphoneMonitor.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly WebScraperService _scraperService;
        private readonly DataAnalysisService _analysisService;
        private readonly DatabaseService _databaseService;
        private readonly ExchangeRateService _exchangeRateService;
        private readonly DispatcherTimer _autoMonitorTimer;
        private readonly Dispatcher _dispatcher;
        private readonly Random _random = new Random();

        private List<Listing>? _allScrapedListings;
        private CancellationTokenSource? _cts;

        private bool _isBusy;
        private string _status = "Готов к работе";
        private int _maxPages = 5;
        private bool _fetchDetails = true;
        private bool _isAutoMonitoring;
        private string _selectedIntervalString = "5 минут";
        
        private AnalysisResult _result = new AnalysisResult();
        private bool _sortByViews;
        private int _minViews;
        
        private string _selectedBrandFilter = "Все бренды";
        private string _searchKeyword = string.Empty;
        private string _selectedSortOption = "По умолчанию";
        
        private string _newBlacklistNumber = string.Empty;
        private string _newBlacklistReason = string.Empty;
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

        public ObservableCollection<string> IntervalOptions { get; } = new ObservableCollection<string>
        {
            "5 минут", "10 минут", "15 минут", "30 минут", "60 минут"
        };

        public AnalysisResult Result
        {
            get => _result;
            set
            {
                if (SetProperty(ref _result, value))
                {
                    OnPropertyChanged(nameof(HasResult));
                    OnPropertyChanged(nameof(HasViewsData));
                    OnPropertyChanged(nameof(HasHotDeals));
                    OnPropertyChanged(nameof(HasChronicSellers));
                    UpdateBrandFilterOptions();
                    RefreshFilteredLists();
                }
            }
        }

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

        public ObservableCollection<BlacklistEntry> Blacklist { get; } = new ObservableCollection<BlacklistEntry>();
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

        public MainViewModel()
        {
            _scraperService = new WebScraperService();
            _analysisService = new DataAnalysisService();
            _databaseService = new DatabaseService();
            _exchangeRateService = new ExchangeRateService();
            _dispatcher = Dispatcher.CurrentDispatcher;

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

            SetMinViewsCommand = new RelayCommand(p =>
            {
                if (p != null && int.TryParse(p.ToString(), out int min))
                {
                    MinViews = min;
                }
            });

            // Load saved settings
            LoadSavedSettings();

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
        }

        private void CancelScan()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                LogMessage("⏹️ Сканирование отменено пользователем.");
                Status = "Отмена...";
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
                LogMessage($"⏱️ Авто-мониторинг запущен с интервалом {_selectedIntervalString}");
            }
            else
            {
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

            _cts = new CancellationTokenSource();
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
                    var semaphore = new SemaphoreSlim(3);
                    var tasks = rawListings.Select(async l =>
                    {
                        await semaphore.WaitAsync(token);
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            // Add a random delay to prevent concurrent burst requests
                            await Task.Delay(_random.Next(200, 600), token);
                            var details = await _scraperService.FetchPhoneAsync(l.Url, token);
                            l.PhoneNumber = details.phone;
                            l.SellerName = details.seller;
                            l.Description = details.description;
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

                token.ThrowIfCancellationRequested();

                // Save new price history
                _databaseService.SavePriceHistory(rawListings);

                // Run data analysis
                Status = "Анализ данных...";
                var blacklistPhones = Blacklist.Select(b => b.PhoneNumber).ToList();
                var analysisResult = await Task.Run(() => _analysisService.Analyze(rawListings, blacklistPhones, preRunHistory), token);

                Result = analysisResult;

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

                // Brand filter
                if (SelectedBrandFilter != "Все бренды")
                {
                    query = query.Where(l => l.Brand == SelectedBrandFilter);
                }

                // Keyword search
                if (!string.IsNullOrWhiteSpace(SearchKeyword))
                {
                    string k = SearchKeyword.Trim().ToLowerInvariant();
                    query = query.Where(l => l.Title.ToLowerInvariant().Contains(k) || l.Description.ToLowerInvariant().Contains(k) || l.Model.ToLowerInvariant().Contains(k));
                }

                // Sort option
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
                string norm = DataAnalysisService.NormalizePhone(phone);
                if (string.IsNullOrEmpty(norm)) return;

                _dispatcher.Invoke(() =>
                {
                    if (Blacklist.Any(b => DataAnalysisService.NormalizePhone(b.PhoneNumber) == norm))
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
                    Task.Run(() =>
                    {
                        var blacklistPhones = _dispatcher.Invoke(() => Blacklist.Select(b => b.PhoneNumber).ToList());
                        var analysisResult = _analysisService.Analyze(_allScrapedListings, blacklistPhones, _databaseService.GetPriceHistory());
                        _dispatcher.Invoke(() =>
                        {
                            Result = analysisResult;
                            UpdateViewsDistribution(analysisResult.AllPrivateListings);
                        });
                    });
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
                    Task.Run(() =>
                    {
                        var blacklistPhones = _dispatcher.Invoke(() => Blacklist.Select(b => b.PhoneNumber).ToList());
                        var analysisResult = _analysisService.Analyze(_allScrapedListings, blacklistPhones, _databaseService.GetPriceHistory());
                        _dispatcher.Invoke(() =>
                        {
                            Result = analysisResult;
                            UpdateViewsDistribution(analysisResult.AllPrivateListings);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка удаления из ЧС: {ex.Message}");
            }
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
