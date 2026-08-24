using System;
using System.Text;

namespace SmartphoneMonitor.Services
{
    internal static class PromoKeyHelper
    {
        public static string SanitizeKey(string brand, string model, int storage = 0)
        {
            string b = NormalizeToken(brand);
            string m = NormalizeToken(model);
            if (storage > 0)
            {
                return $"{b}_{m}_{storage}";
            }
            return $"{b}_{m}";
        }

        public static string SanitizePrefix(string brand)
        {
            return NormalizeToken(brand) + "_";
        }

        private static string NormalizeToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var sb = new StringBuilder();
            foreach (var ch in input.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-') sb.Append('_');
                // skip other punctuation
            }
            // collapse multiple underscores
            var normalized = sb.ToString();
            while (normalized.Contains("__")) normalized = normalized.Replace("__", "_");
            return normalized.Trim('_');
        }
    }
}
