using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using ScanOutTool.Models;
using ScanOutTool.Services;
using ScanOutTool.ViewModels.Controls;
using System.Collections.Generic;

namespace ScanOutTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly IAppState _appState;
        private readonly IThemeService _themeService;

        public List<string> ThemeModes { get; } = new() { "Dark", "Light" };

        [ObservableProperty] private SerialPortSettingViewModel scannerPortSettingVM;      

        [ObservableProperty] private SerialPortSettingViewModel shopFloorPortSettingVM;

        [ObservableProperty] private bool isWOMode;

        [ObservableProperty] private bool beepOnScanReceived;

        [ObservableProperty] private string serverIP;

        [ObservableProperty] private string xrayApiServer;

        [ObservableProperty] private string xrayApiPort;

        [ObservableProperty] private string shopFloorExePath;

        [ObservableProperty] private string shopFloorLogPath; 

        [ObservableProperty] private string selectedThemeMode;

        public bool CanEdit => !_appState.IsRunning;

        [RelayCommand]
        private void Save()
        {
            _configService.Config.ScannerPortSettingVM = ScannerPortSettingVM;
            _configService.Config.ShopFloorPortSettingVM = ShopFloorPortSettingVM;
            _configService.Config.ServerIP = ServerIP;
            _configService.Config.XrayApiServer = XrayApiServer;
            _configService.Config.XrayApiPort = XrayApiPort;
            _configService.Config.IsWOMode = IsWOMode;
            _configService.Config.BeepOnScanReceived = BeepOnScanReceived;
            _configService.Config.ShopFloorExePath = ShopFloorExePath;
            _configService.Config.ShopFloorLogPath = ShopFloorLogPath;
            _configService.Config.ThemeMode = SelectedThemeMode;
            _configService.Save();
            _themeService.ApplyTheme(SelectedThemeMode);
        }

        [RelayCommand]
        private void LogBrowser()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                ShopFloorLogPath = dialog.FileName;
            }
        }

        [RelayCommand]
        private void ShopFloorBrowser()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.Filters.Add(new CommonFileDialogFilter("Executable", "*.exe"));
            dialog.Filters.Add(new CommonFileDialogFilter("All files", "*.*"));

            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                ShopFloorExePath = dialog.FileName;
            }
        }


        public SettingsViewModel(IConfigService configService,IAppState appState, IThemeService themeService)
        {
            _configService = configService;
            _appState = appState;
            _themeService = themeService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            ScannerPortSettingVM = _configService.Config.ScannerPortSettingVM?? new SerialPortSettingViewModel();
            ShopFloorPortSettingVM = _configService.Config.ShopFloorPortSettingVM?? new SerialPortSettingViewModel();
            IsWOMode = _configService.Config.IsWOMode;
            BeepOnScanReceived = _configService.Config.BeepOnScanReceived;
            ServerIP = _configService.Config.ServerIP;
            XrayApiServer = _configService.Config.XrayApiServer;
            XrayApiPort = _configService.Config.XrayApiPort;
            ShopFloorExePath = _configService.Config.ShopFloorExePath;
            ShopFloorLogPath = _configService.Config.ShopFloorLogPath ?? "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
            SelectedThemeMode = string.IsNullOrWhiteSpace(_configService.Config.ThemeMode)
                ? "Dark"
                : _configService.Config.ThemeMode;
        }
    }
}
