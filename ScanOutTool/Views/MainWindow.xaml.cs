using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using ScanOutTool.Services;
using ScanOutTool.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ScanOutTool.Views
{
    public partial class MainWindow : Window
    {
        private TaskbarIcon _trayIcon;
        public MainWindow(MainViewModel vm, INavigationService navigationService)
        {
            InitializeComponent();
            DataContext = vm;
            navigationService.SetFrame(MainFrame);            
            navigationService.NavigateTo<DashboardPage>();
            vm.PageName = "Dashboard".ToUpper();
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyMaximizedWorkAreaBounds();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            ApplyMaximizedWorkAreaBounds();
        }

        private void ApplyMaximizedWorkAreaBounds()
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this, "Bạn có muốn ẩn chương trình xuống khay hệ thống?",
                                         "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                _trayIcon.Dispose();
                Application.Current.Shutdown();
            }
            else
            {
                this.Hide();
                //this.WindowState = WindowState.Minimized;
                //this.ShowInTaskbar = false;

                _trayIcon.Visibility = Visibility.Visible; // Cực kỳ quan trọng
            }
        }

    }
}
