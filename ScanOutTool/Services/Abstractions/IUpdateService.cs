using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IUpdateService
    {
        Task CheckForUpdatesAsync();
    }
}
