using System;

namespace SmartphoneMonitor.Models
{
    public class BlacklistEntry
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }
}
