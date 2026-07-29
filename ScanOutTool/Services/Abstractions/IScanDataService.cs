using DataExcuter;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IScanDataService
    {
        bool ShouldClearAfterSend { get; }
        event EventHandler<string> StatusChanged;
        Task<bool> StartSessionAsync();
        (bool IsBlocked, string Reason) IsBlocked(string pid);
        Task<ProcessResult> ProcessDataAsync(string workOrder, string partNo, string pid, bool validateDatabase = true, bool transferToWeb = true);
        Task<int> GetPackQty();
        Task<int> GetMagazineQty();
        Task<RescanInfo> GetLastRescanInfo();
        Task<bool> PrintManual();
    }
}
