using System;

namespace SmartphoneMonitor.Models
{
    public class BlacklistLoginEntry
    {
        public string Login { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }
}
