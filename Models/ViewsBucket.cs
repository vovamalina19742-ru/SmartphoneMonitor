namespace SmartphoneMonitor.Models
{
    public class ViewsBucket
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Fraction { get; set; }

        public string CountLabel
        {
            get
            {
                if (Count != 0)
                {
                    return Count.ToString();
                }
                return "";
            }
        }
    }
}
