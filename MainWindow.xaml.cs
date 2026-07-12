using System.Windows;

namespace SmartphoneMonitor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new SmartphoneMonitor.ViewModels.MainViewModel();
        }
    }
}
