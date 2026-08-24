using System.Collections.Generic;
using System.Linq;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    /// <summary>
    /// Detects chronic sellers (those with 3+ listings or unique brands).
    /// </summary>
    public class ChronicSellerService
    {
        public List<ChronicSeller> Detect(List<Listing> listings)
        {
            var result = new List<ChronicSeller>();

            var chronicGroups = from listing in listings
                                where !string.IsNullOrEmpty(listing.PhoneNumber)
                                group listing by ListingClassifier.NormalizePhone(listing.PhoneNumber) into g
                                where g.Count() >= 3
                                select g;

            foreach (var group in chronicGroups)
            {
                int uniqueBrands = group.Select(l => l.Brand).Distinct().Count();
                result.Add(new ChronicSeller
                {
                    PhoneNumber = group.Key,
                    SellerName = group.First().SellerName,
                    ListingCount = group.Count(),
                    UniqueBrands = uniqueBrands,
                    Reason = uniqueBrands >= 3
                        ? $"Продаёт {uniqueBrands} разных бренда(ов), {group.Count()} объявлений"
                        : $"Хронический продавец: {group.Count()} объявлений"
                });
            }

            return result;
        }
    }
}