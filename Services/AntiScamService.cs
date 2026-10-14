using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public class PhotoHashRecord
    {
        public ulong PrimaryHash { get; set; }
        public ulong DHash { get; set; }
        public List<ulong> CropHashes { get; set; } = new();
        public string ListingUrl { get; set; } = string.Empty;
        public string AuthorLogin { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.Now;
    }

    public class AntiScamService
    {
        private const int N = 32;
        public const int DefaultMaxDistance = 8;
        public const int DefaultCropMaxDistance = 10;

        private static readonly double[,] CosTable = BuildCosTable();
        private static readonly double[] AlphaTable = BuildAlphaTable();
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly SemaphoreSlim _downloadSemaphore = new(6);

        // База известных отпечатков фотографий: PrimaryHash -> PhotoHashRecord
        private readonly ConcurrentDictionary<ulong, PhotoHashRecord> _hashDatabase = new();
        private readonly DatabaseService? _databaseService;

        public AntiScamService(DatabaseService? databaseService = null)
        {
            _databaseService = databaseService;
            if (_databaseService != null)
            {
                try
                {
                    var persistentHashes = _databaseService.LoadPhotoHashes();
                    foreach (var record in persistentHashes)
                    {
                        _hashDatabase.TryAdd(record.PrimaryHash, record);
                    }
                }
                catch
                {
                    // Игнорируем ошибки предзагрузки базы
                }
            }
        }

        private static double[,] BuildCosTable()
        {
            var table = new double[N, N];
            for (int u = 0; u < N; u++)
            {
                for (int x = 0; x < N; x++)
                {
                    table[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / (2.0 * N));
                }
            }
            return table;
        }

        private static double[] BuildAlphaTable()
        {
            var table = new double[N];
            for (int k = 0; k < N; k++)
            {
                table[k] = k == 0 ? Math.Sqrt(1.0 / N) : Math.Sqrt(2.0 / N);
            }
            return table;
        }

        // Вычисление 64-битного pHash из BitmapSource с исключением DC [0,0]
        public static ulong ComputePHashFromFrame(BitmapSource frame)
        {
            var grayBitmap = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
            var scaledBitmap = new TransformedBitmap(grayBitmap, new ScaleTransform((double)N / frame.PixelWidth, (double)N / frame.PixelHeight));

            byte[] pixels = new byte[N * N];
            scaledBitmap.CopyPixels(pixels, N, 0);

            var luminance = new double[N, N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    luminance[y, x] = pixels[y * N + x];
                }
            }

            // 2. Двумерный DCT-II
            double[,] dct = Dct2D(luminance);

            // 3. Выделение низкочастотного блока 8x8
            Span<double> lows = stackalloc double[64];
            for (int u = 0; u < 8; u++)
            {
                for (int v = 0; v < 8; v++)
                {
                    lows[u * 8 + v] = dct[u, v];
                }
            }

            // 4. Расчет медианы блока БЕЗ постоянной составляющей DC [0,0] (Slice 1..63)
            double median = CalculateMedian(lows.Slice(1));

            // 5. Формирование 64-битного хэша (1 если > медианы, 0 если <=)
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
            {
                if (lows[i] > median)
                {
                    hash |= 1UL << i;
                }
            }
            return hash;
        }

        // Вычисление 64-битного разностного dHash (градиент 9x8)
        public static ulong ComputeDHash(BitmapSource frame)
        {
            var grayBitmap = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
            var scaledBitmap = new TransformedBitmap(grayBitmap, new ScaleTransform(9.0 / frame.PixelWidth, 8.0 / frame.PixelHeight));

            byte[] pixels = new byte[9 * 8];
            scaledBitmap.CopyPixels(pixels, 9, 0);

            ulong hash = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (pixels[y * 9 + x] > pixels[y * 9 + x + 1])
                    {
                        hash |= 1UL << (y * 8 + x);
                    }
                }
            }
            return hash;
        }

        // Составной 128-битный хэш: (pHash, dHash)
        public static (ulong pHash, ulong dHash) ComputeCompositeHash(Stream imageStream)
        {
            var decoder = BitmapDecoder.Create(imageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            ulong pHash = ComputePHashFromFrame(frame);
            ulong dHash = ComputeDHash(frame);
            return (pHash, dHash);
        }

        // Обратная совместимость с вызовом ComputeHash(Stream)
        public static ulong ComputeHash(Stream imageStream)
        {
            return ComputeCompositeHash(imageStream).pHash;
        }

        public static int HammingDistance(ulong a, ulong b)
        {
            return BitOperations.PopCount(a ^ b); // Нативная аппаратная инструкция POPCNT
        }

        public static double SimilarityPercent(ulong a, ulong b)
        {
            return (64 - HammingDistance(a, b)) * 100.0 / 64.0;
        }

        private static double[,] Dct2D(double[,] input)
        {
            var byRows = new double[N, N];
            var frequency = new double[N, N];

            for (int y = 0; y < N; y++)
            {
                for (int u = 0; u < N; u++)
                {
                    double sum = 0;
                    for (int x = 0; x < N; x++)
                    {
                        sum += input[y, x] * CosTable[u, x];
                    }
                    byRows[y, u] = sum * AlphaTable[u];
                }
            }

            for (int u = 0; u < N; u++)
            {
                for (int v = 0; v < N; v++)
                {
                    double sum = 0;
                    for (int y = 0; y < N; y++)
                    {
                        sum += byRows[y, u] * CosTable[v, y];
                    }
                    frequency[u, v] = sum * AlphaTable[v];
                }
            }
            return frequency;
        }

        private static double CalculateMedian(Span<double> values)
        {
            double[] copy = values.ToArray();
            Array.Sort(copy);
            int mid = copy.Length >> 1;
            return (copy.Length & 1) == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2.0;
        }

        // Проверка листинга на совпадение фото с чужими объявлениями
        public async Task AnalyzeListingPhotosAsync(Listing listing)
        {
            if (listing.ImageUrls == null || listing.ImageUrls.Count == 0) return;

            foreach (var imgUrl in listing.ImageUrls.Take(3)) // Проверяем первые 3 ключевых фото
            {
                byte[]? imageBytes = null;
                await _downloadSemaphore.WaitAsync();
                try
                {
                    imageBytes = await _httpClient.GetByteArrayAsync(imgUrl);
                }
                catch
                {
                    // Игнорируем сетевые ошибки загрузки единичных превью
                }
                finally
                {
                    _downloadSemaphore.Release();
                }

                if (imageBytes == null || imageBytes.Length == 0) continue;

                try
                {
                    using var ms = new MemoryStream(imageBytes);
                    var (currentPHash, currentDHash) = ComputeCompositeHash(ms);
                    listing.PhotoHashes.Add(currentPHash);

                    // Сверяем с базой известных отпечатков
                    foreach (var kvp in _hashDatabase)
                    {
                        var record = kvp.Value;
                        if (record.ListingUrl == listing.Url) continue; // Собственное объявление

                        int pDistance = HammingDistance(currentPHash, record.PrimaryHash);
                        int dDistance = record.DHash > 0 ? HammingDistance(currentDHash, record.DHash) : pDistance;

                        // Если расстояние Хэмминга по pHash <= 8 ИЛИ по dHash <= 8
                        if (pDistance <= DefaultMaxDistance || (record.DHash > 0 && dDistance <= DefaultMaxDistance))
                        {
                            // Если автор отличается — это подозрительный перезалив чужого фото!
                            if (!string.IsNullOrEmpty(record.AuthorLogin) &&
                                !string.Equals(record.AuthorLogin, listing.AuthorLogin, StringComparison.OrdinalIgnoreCase))
                            {
                                listing.IsDuplicatePhotoDetected = true;
                                listing.DuplicateSourceInfo = $"Совпадение {SimilarityPercent(currentPHash, record.PrimaryHash):F0}% с объявлением {record.ListingUrl} (продавец {record.AuthorLogin})";
                                break;
                            }
                        }
                    }

                    var newRecord = new PhotoHashRecord
                    {
                        PrimaryHash = currentPHash,
                        DHash = currentDHash,
                        ListingUrl = listing.Url,
                        AuthorLogin = listing.AuthorLogin,
                        Price = listing.PriceValue,
                        FirstSeen = DateTime.Now
                    };

                    // Регистрируем хэш в память и сохраняем в базу SQLite
                    if (_hashDatabase.TryAdd(currentPHash, newRecord))
                    {
                        _databaseService?.SavePhotoHash(newRecord);
                    }
                }
                catch
                {
                    // Защита от поврежденных форматов изображений
                }
            }
        }
    }
}
