using ScanOutTool.Models;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IScanDataService
    {
        Task<(bool IsBlocked, string Reason)> IsBlockedAsync(string pid);
        Task<PushResultDto> PushAvPidOutputAsync(AvPidInoutDto item);
    }
}
