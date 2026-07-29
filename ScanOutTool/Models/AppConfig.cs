using ScanOutTool.ViewModels.Controls;

namespace ScanOutTool.Models
{
    public class AppConfig
    {
        public SerialPortSettingViewModel ScannerPortSettingVM { get; set; }
        public SerialPortSettingViewModel ShopFloorPortSettingVM { get; set; }
        public bool IsWOMode { get; set; }
        public string ServerIP { get; set; }
        public string ShopFloorExePath { get; set; }
        public string ShopFloorLogPath { get; set; } = "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
    }
}
