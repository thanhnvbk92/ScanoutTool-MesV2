using ScanOutTool.Models;

namespace ScanOutTool.Services
{
    public interface IConfigService
    {
        AppConfig Config { get; }
        void Save();
        void Reload();
    }
}
