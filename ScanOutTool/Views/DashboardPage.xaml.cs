using ScanOutTool.ViewModels;
using System.Windows.Controls;

namespace ScanOutTool.Views
{
    public partial class DashboardPage : Page
    {
        public DashboardPage(DashboardViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogTextBox.ScrollToEnd();
        }
    }
}
