using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutTool.Services;

namespace ScanOutTool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;
        private readonly IUpdateService _updateService;

        [ObservableProperty]
        private string title;

        [ObservableProperty]
        private bool isSidebarOpen;

        [ObservableProperty]
        private int selectedMenuIndex;

        [ObservableProperty]
        private string pageName;

        public MainViewModel(INavigationService navigationService, IUpdateService updateService)
        {
            _navigationService = navigationService;
            _updateService = updateService;
            IsSidebarOpen = true;
            //SelectedMenuIndex = 0;

            Title = $"Scan Out Tool v{getVersion()}";
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarOpen = !IsSidebarOpen;
        }

        partial void OnSelectedMenuIndexChanged(int value)
        {
            switch (value)
            {
                case 0:
                    _navigationService.NavigateTo<Views.DashboardPage>();
                    PageName = "DASHBOARD";
                    break;
                case 1:
                    _navigationService.NavigateTo<Views.SettingsPage>();
                    PageName = "SETTING";
                    break;
                case 2:
                    _navigationService.NavigateTo<Views.AboutPage>();
                    PageName = "ABOUT";
                    break;
                case 3:
                    _updateService.CheckForUpdatesAsync();
                    break;
            }
        }

        private string getVersion()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            return assembly.GetName().Version.ToString();
        }

    }
}

