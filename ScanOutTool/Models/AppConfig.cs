using ScanOutTool.ViewModels.Controls;

namespace ScanOutTool.Models
{
    public class AppConfig
    {
        public SerialPortSettingViewModel ScannerPortSettingVM { get; set; }
        public SerialPortSettingViewModel ShopFloorPortSettingVM { get; set; }
        public bool IsWOMode { get; set; }
        public string ServerIP { get; set; }
        public string QaBlockApiServer { get; set; } = "http://10.7.10.6:9999";
        public string QaMachineName { get; set; } = string.Empty;
        public string XrayApiServer { get; set; } = string.Empty;
        public string XrayApiPort { get; set; } = string.Empty;
        public string ShopFloorExePath { get; set; }
        public string ShopFloorLogPath { get; set; } = "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
        public bool BeepOnScanReceived { get; set; } = true;
        public string LastRunMode { get; set; } = nameof(RunMode.ScanOut_Rescan);
        public string ThemeMode { get; set; } = "Dark";
    }
}
