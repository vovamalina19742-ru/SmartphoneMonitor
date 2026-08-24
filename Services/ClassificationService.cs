using System.Collections.Generic;
using System.Linq;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Services
{
    /// <summary>
    /// Separates listings into commercial and private, applies blacklist filtering.
    /// </summary>
    public class ClassificationService
    {
        public ClassificationResult Classify(List<Listing> listings, List<string> blacklist, List<string> blacklistLogins)
        {
            var privateListings = new List<Listing>();
            var commercialListings = new List<Listing>();
            int filteredByBlacklist = 0;

            foreach (var listing in listings)
            {
                string phone = ListingClassifier.NormalizePhone(listing.PhoneNumber);
                bool isBlacklisted = (!string.IsNullOrEmpty(phone) && blacklist.Any(b => ListingClassifier.NormalizePhone(b) == phone)) ||
                                     (!string.IsNullOrEmpty(listing.AuthorLogin) && blacklistLogins.Contains(listing.AuthorLogin));

                if (isBlacklisted)
                {
                    filteredByBlacklist++;
                }

                bool isCommercial = listing.IsCommercial || listing.SellerType == "Shop" || isBlacklisted ||
                                    ListingClassifier.IsCommercial(listing.Title + " " + listing.Description, listing.SellerType, listing.AuthorLogin, listing.PhoneNumber, blacklist, blacklistLogins);
                listing.IsCommercial = isCommercial;
                listing.IsUrgent = ListingClassifier.IsUrgent(listing.Title + " " + listing.Description);

                if (isCommercial)
                    commercialListings.Add(listing);
                else
                    privateListings.Add(listing);
            }

            return new ClassificationResult
            {
                PrivateListings = privateListings,
                CommercialListings = commercialListings,
                FilteredByBlacklist = filteredByBlacklist
            };
        }
    }

    public class ClassificationResult
    {
        public List<Listing> PrivateListings { get; set; } = new();
        public List<Listing> CommercialListings { get; set; } = new();
        public int FilteredByBlacklist { get; set; }
    }
}