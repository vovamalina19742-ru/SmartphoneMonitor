using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    public static class ListingClassifier
    {
        public const string ParserVersion = "1.0.0";
        public const string ParserSource = "999.md GraphQL";

        public static string NormalizePhone(string? phone)
        {
            if (string.IsNullOrEmpty(phone))
            {
                return string.Empty;
            }
            string text = Regex.Replace(phone, "[^\\d]", string.Empty);
            if (text.StartsWith("373"))
            {
                text = text.Substring(3);
            }
            if (text.StartsWith("0"))
            {
                text = text.Substring(1);
            }
            return text;
        }

        public static bool IsAccessoryOrPartOrOther(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;
            string t = title.ToLowerInvariant();

            string[] exclusionKeywords = new string[]
            {
                "чехол", "чехлы", "husa", "huse", "sticla", "sticle", "пленка", "пленки", "folie", "folii",
                "кабель", "cablul", "cablu", "incarcator", "incarcatoare", "casti", "наушники", "airpods",
                "запчасти", "запчасть", "piese", "piese de schimb", "razborca", "разборка", "разбор",
                "детали", "на детали", "дисплей", "ecran", "display", "cutie", "cutii", "коробка", "коробки",
                "box", "icloud blocat", "icloud locked", "блокирован", "блокированый", "locked", "bypass", "mdm",
                "на запчасти", "для запчастей", "pentru piese", "pentru piese de schimb", "blocat icloud",
                "зарядка", "зарядки", "зарядное", "камера", "плата", "платы", "материнская плата", "placa de baza",
                "шлейф", "корпус", "carcasa", "стекло заднее", "sticla spate",
                "watch", "часы", "ceas", "ceasuri", "smartwatch", "smart-watch", "fitbit", "mi band", "smart band",
                "ipad", "планшет", "tableta", "tablet", "стационарный", "домашний телефон", "телефон домашний",
                "telefon fix"
            };

            foreach (var kw in exclusionKeywords)
            {
                if (t.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsBuyAd(string title, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string fullText = (title + " " + (description ?? "")).ToLowerInvariant();

            string[] buyKeywords = new string[]
            {
                "cumpăr", "cumpar", "cumparam", "cumpăram", "cumparom", "cumparati", "cumpărăm",
                "куплю", "скупаю", "покупаю", "скупка", "скупаем", "покупаем",
                "скупка телефонов", "скупка смартфонов", "ищу на запчасти", "caut piese",
                "caut telefon", "caut smartphone", "caut iphone", "купим"
            };

            foreach (var kw in buyKeywords)
            {
                if (Regex.IsMatch(fullText, @"\b" + Regex.Escape(kw) + @"\b", RegexOptions.IgnoreCase) || fullText.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsFeatureOrRetroPhone(string title, string brand = "")
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string t = title.ToLowerInvariant();

            string[] retroKeywords = new string[]
            {
                "кнопочный", "кнопочные", "кнопочник", "buton", "butoane", "сименс", "siemens",
                "nokia 3310", "nokia 6300", "nokia 6700", "nokia 8800", "nokia 105", "nokia 106", "nokia 110",
                "nokia 130", "nokia 150", "nokia 210", "nokia 215", "nokia 220", "nokia 225", "nokia 230", "nokia 5310",
                "nokia 8000", "nokia 6310", "nokia 2720", "nokia c1", "nokia c2", "nokia 2600", "nokia 3110", "nokia 5200",
                "nokia 5300", "nokia 5800", "nokia n73", "nokia n95", "nokia n8", "nokia e71", "nokia e72", "nokia 800",
                "sony ericsson", "сони эрикссон", "motorola razr v3", "fly ", "sigma ", "flyer", "texet", "maxvi", "bqv",
                "zte r", "myphone", "prestigio", "alcatel 10", "alcatel 20", "vertu", "раскладушка", "clamshell"
            };

            foreach (var kw in retroKeywords)
            {
                if (t.Contains(kw))
                {
                    return true;
                }
            }

            if (t.Contains("nokia") && !t.Contains("lumia") && !t.Contains("nokia g") && !t.Contains("nokia c20") && !t.Contains("nokia x") && !t.Contains("nokia 1.") && !t.Contains("nokia 2.") && !t.Contains("nokia 3.") && !t.Contains("nokia 4.") && !t.Contains("nokia 5.") && !t.Contains("nokia 6.") && !t.Contains("nokia 7.") && !t.Contains("nokia 8.") && !t.Contains("nokia 9."))
            {
                if (Regex.IsMatch(t, @"nokia\s+\d{3,4}\b"))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAppleOrIPhone(string title, string brand = "")
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(brand)) return false;
            
            if (!string.IsNullOrWhiteSpace(brand) && brand.Equals("Apple", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string t = (title ?? "").ToLowerInvariant();
            string[] appleKeywords = new string[]
            {
                "iphone", "i phone", "ipone", "ipon", "айфон", "айфоны", "айфончик", "apple"
            };

            foreach (var kw in appleKeywords)
            {
                if (t.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }

        public static int ExtractStorage(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return 0;
            }
            var match = Regex.Match(title, "\\b(32|64|128|256|512)\\s*[Gg][Bb]\\b");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int storage))
            {
                return storage;
            }
            if (Regex.IsMatch(title, "\\b1\\s*[Tt][Bb]\\b"))
            {
                return 1024;
            }
            var tbMatch = Regex.Match(title, "\\b(2|3|4|5)\\s*[Tt][Bb]\\b");
            if (tbMatch.Success && int.TryParse(tbMatch.Groups[1].Value, out int tbValue))
            {
                return tbValue * 1024;
            }
            return 0;
        }

        public static string DetectBrand(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Другие";
            }
            string text = title.ToLowerInvariant();
            if (text.Contains("iphone") || text.Contains("apple"))
            {
                return "Apple";
            }
            if (text.Contains("samsung") || text.Contains("galaxy") || Regex.IsMatch(text, "\\bgalaxy\\s+[aszfm]\\d"))
            {
                return "Samsung";
            }
            if (text.Contains("redmi") || text.Contains("poco") || text.Contains("xiaomi"))
            {
                return "Xiaomi";
            }
            if (text.Contains("honor"))
            {
                return "Honor";
            }
            if (text.Contains("huawei") || text.Contains("p smart") || Regex.IsMatch(text, "\\b(mate\\s*\\d+|nova\\s*\\d+|p\\d{2}\\s*(pro|lite)?)\\b"))
            {
                return "Huawei";
            }
            if (text.Contains("realme"))
            {
                return "Realme";
            }
            if (text.Contains("oneplus") || text.Contains("one plus"))
            {
                return "OnePlus";
            }
            if (text.Contains("oppo"))
            {
                return "OPPO";
            }
            if (text.Contains("nokia"))
            {
                return "Nokia";
            }
            if (text.Contains("motorola") || text.Contains("moto g") || text.Contains("moto e") || text.Contains("moto "))
            {
                return "Motorola";
            }
            if (text.Contains("sony") || text.Contains("xperia"))
            {
                return "Sony";
            }
            if (text.Contains("vivo"))
            {
                return "Vivo";
            }
            if (text.Contains("tecno"))
            {
                return "Tecno";
            }
            if (text.Contains("infinix"))
            {
                return "Infinix";
            }
            if (Regex.IsMatch(text, "\\blg\\b"))
            {
                return "LG";
            }
            if (text.Contains("asus") || text.Contains("zenfone") || text.Contains("rog phone"))
            {
                return "Asus";
            }
            if (text.Contains("lenovo"))
            {
                return "Lenovo";
            }
            if (text.Contains("zte"))
            {
                return "ZTE";
            }
            if (text.Contains("alcatel"))
            {
                return "Alcatel";
            }
            if (text.Contains("meizu"))
            {
                return "Meizu";
            }
            if (text.Contains("google pixel") || text.Contains("pixel "))
            {
                return "Google";
            }
            if (text.Contains("nothing phone") || text.Contains("nothing"))
            {
                return "Nothing";
            }
            if (text.Contains("blackview") || text.Contains("oukitel") || text.Contains("ulefone") || text.Contains("doogee"))
            {
                return "Rugged";
            }
            return "Другие";
        }

        public static bool IsUrgent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string lower = text.ToLowerInvariant();
            return Constants.UrgencyMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCommercial(string title, string sellerType, string authorLogin, string phoneNumber, IEnumerable<string> blacklistPhones, IEnumerable<string> blacklistLogins)
        {
            if (!string.IsNullOrEmpty(sellerType) && sellerType.Equals("Shop", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(authorLogin) && blacklistLogins.Contains(authorLogin, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedPhone = NormalizePhone(phoneNumber);
            if (!string.IsNullOrEmpty(normalizedPhone) && blacklistPhones.Any(b => NormalizePhone(b).Equals(normalizedPhone, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string text = (title ?? string.Empty).ToLowerInvariant();
            return Constants.CommercialMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsResellerShowcase(string title, string? description, string authorLogin, int authorListingCount)
        {
            if (authorListingCount >= 2)
            {
                return true;
            }

            string text = ((title ?? "") + " " + (description ?? "")).ToLowerInvariant();

            string[] resellerPatterns = new string[]
            {
                "9.9/10", "10/10", "9,9/10", "9.8/10", "9.5/10", "stare 10/10", "stare 9.9/10",
                "stare ideala", "состояние 9.9", "состояние 10/10", "как новый 9.9", "9.9 из 10", "10 из 10",
                "vitrina", "витрина", "din vitrina", "с витрины", "schimb", "обмен",
                "in credit", "в кредит", "credit 0%", "rate 0%", "trade in", "trade-in",
                "garantie magazin", "гарантия магазина", "livrare toata moldova", "livrare rapida",
                "toata tara", "husa cadou", "sticla cadou", "чехол в подарок", "стекло в подарок"
            };

            return resellerPatterns.Any(p => text.Contains(p));
        }

        public static string DetermineNewSmartphoneCategory(string sellerName, string sellerType, bool isCommercial, bool isNew, string url = "")
        {
            string normSeller = (sellerName ?? "").ToLowerInvariant();
            string normUrl = (url ?? "").ToLowerInvariant();

            if (normSeller.Contains("orange") || normSeller.Contains("moldcell") || normSeller.Contains("enter") ||
                normSeller.Contains("darwin") || normSeller.Contains("bomba") || normSeller.Contains("maximum") ||
                normSeller.Contains("ultra") || normUrl.Contains("orange.md") || normUrl.Contains("moldcell.md") ||
                normUrl.Contains("enter.online") || normUrl.Contains("darwin.md"))
            {
                return "RetailChain";
            }

            if (sellerType != null && sellerType.Equals("RESELLER", StringComparison.OrdinalIgnoreCase))
            {
                return "Reseller";
            }

            if (sellerType != null && sellerType.Equals("FRESH_PRIVATE", StringComparison.OrdinalIgnoreCase))
            {
                return "FreshPrivate";
            }

            if ((isCommercial || (sellerType != null && sellerType.Equals("Shop", StringComparison.OrdinalIgnoreCase))) && isNew)
            {
                return "Shop999";
            }

            if (isCommercial)
            {
                return "Reseller";
            }

            if (!isCommercial && isNew)
            {
                return "PrivateNew";
            }

            return "PrivateUsed";
        }
    }
}
