using ScanOutTool.Models;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IShopFloorService
    {
        Task EnsureRunningAsync();
        Task RestartAsync();
        Task InitializeAsync();
        bool IsScanoutUI();
        bool ClickClear();
        bool ClickTrace();
        ShopFloorScanResult ReadCurrent();
        Task<ShopFloorScanResult> WaitForResultAsync(string pid, int timeoutMs = 5000);
    }
}
