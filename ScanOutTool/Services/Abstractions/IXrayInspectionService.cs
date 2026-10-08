using ScanOutTool.Models;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IXrayInspectionService
    {
        Task<XrayTraceResult> GetBlockingResultAsync(string pid);
    }
}
