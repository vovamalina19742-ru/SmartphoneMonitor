namespace SmartphoneMonitor.Models
{
    public static class Constants
    {
        public static decimal EurToMdl = 19.5m;

        public static readonly string[] CommercialMarkers = new string[]
        {
            "magazin", "shop", "credit", "rate", "garantie", "гарантия",
            "livrare", "доставка", "piese", "запчасти"
        };

        public static readonly string[] UrgencyMarkers = new string[9]
        {
            "urgent", "срочно", "ieftin", "дешево", "super pret", "super-pret", "супер цена", "супер-цена", "отдам"
        };
    }
}
