using ScanOutTool.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ScanOutTool.Views
{
    /// <summary>
    /// Interaction logic for DashboardPage.xaml
    /// </summary>
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

        private void ManualMagazineNoTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel viewModel)
            {
                viewModel.IsManualMagazineInputFocused = true;
            }
        }

        private void ManualMagazineNoTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel viewModel)
            {
                viewModel.IsManualMagazineInputFocused = false;
            }
        }

        //private void LogTextBlock_TargetUpdated(object sender, DataTransferEventArgs e)
        //{
        //    LogScrollViewer.ScrollToEnd();
        //}
    }
}
