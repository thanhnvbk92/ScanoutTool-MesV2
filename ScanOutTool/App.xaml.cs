using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Configuration;
using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScanOutTool.Services;
using ScanOutLogLib.Services;
using ScanOutTool.Views;


namespace ScanOutTool
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private static EventWaitHandle _event;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static IServiceProvider Services { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            string MutexName = Process.GetCurrentProcess().ProcessName +"_Mutex";
            string EventName = Process.GetCurrentProcess().ProcessName + "_Event";
            bool isNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out isNew);
            }
            catch (AbandonedMutexException)
            {
                // Một tiến trình đã crash mà không release mutex → vẫn cho chạy tiếp
                _mutex = new Mutex(true, MutexName);
                isNew = true; // Treat as new instance
            }

            if (!isNew)
            {
                // Gửi tín hiệu yêu cầu hiển thị lại
                try
                {
                    EventWaitHandle.OpenExisting(EventName).Set();
                }
                catch { }

                Shutdown();
                return;
            }

            // Tạo sự kiện chờ yêu cầu hiển thị lại
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            Task.Run(() =>
            {
                while (true)
                {
                    _event.WaitOne(); // chờ tín hiệu
                    Dispatcher.Invoke(() =>
                    {
                        var mw = Current.MainWindow;
                        if (mw != null)
                        {
                            mw.Show();
                            mw.WindowState = WindowState.Normal;
                            mw.Activate();
                        }
                    });
                }
            });



            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();
            Services.GetRequiredService<IThemeService>()
                .ApplyTheme(Services.GetRequiredService<IConfigService>().Config.ThemeMode);

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                MessageBox.Show(Current.MainWindow, $"Lỗi không xử lý: {sender}\r\n" + args.ExceptionObject);
            };

            
            // Tạo MainWindow từ DI
            var mainWindow = Services.GetRequiredService<Views.MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string exeDirectory = Path.GetDirectoryName(exePath);
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(Path.Combine(exeDirectory, "Resources/appsettings.json"), optional: true, reloadOnChange: true)
                .Build();

            Configuration = config;
            services.AddSingleton<IConfiguration>(config);

            services.AddLogging(builder => builder.AddConsole());

            // ViewModels
            services.AddTransient<ViewModels.MainViewModel>();
            services.AddSingleton<ViewModels.DashboardViewModel>();
            services.AddTransient<ViewModels.SettingsViewModel>();
            services.AddTransient<ViewModels.AboutViewModel>();

            // Services
            services.AddSingleton<Services.INavigationService, Services.NavigationService>();
            services.AddSingleton<Services.ILoggingService, Services.LoggingService>();
            services.AddSingleton<Services.IUpdateService, Services.UpdateService>();
            services.AddSingleton<Services.IConfigService, Services.ConfigService>();
            services.AddSingleton<Services.IThemeService, Services.ThemeService>();
            services.AddSingleton<Models.IAppState, Models.AppState>();
            services.AddSingleton<IBlockRFService, BlockRFService>();
            services.AddSingleton<IAudioNotificationService, AudioNotificationService>();
            services.AddSingleton<IShopFloorService, ShopFloorService>();
            services.AddSingleton<IScanDataService, ScanDataService>();
            services.AddSingleton<ICleberMesService, CleberMesService>();
            services.AddSingleton<IXrayInspectionService, XrayInspectionService>();
            //services.AddSingleton<IShowRescanResultService, ShowRescanResultService>();
            //services.AddSingleton<IAutoScanOutUI, AutoScanOutUI>();


            services.AddSingleton<IScanResultService, ScanResultService>();

            // Views
            services.AddTransient<Views.MainWindow>();
            services.AddTransient<Views.DashboardPage>();
            services.AddTransient<Views.SettingsPage>();
            services.AddTransient<Views.AboutPage>();
            services.AddTransient<Views.RescanInfoWindow>();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        private void Menu_Show_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        private void Menu_Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BringExistingWindowToFront()
        {
            var current = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(current.ProcessName);

            foreach (var process in processes)
            {
                if (process.Id != current.Id)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);       // nếu đang minimize
                        SetForegroundWindow(hWnd);         // đưa lên foreground
                    }
                    break;
                }
            }
        }
    }
}
