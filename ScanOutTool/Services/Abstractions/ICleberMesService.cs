using ScanOutTool.Models;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface ICleberMesService
    {
        Task<CleberMesScanResult> GetStatusAsync();
        Task<CleberMesScanResult> GetStatusAsync(string pid);

        Task<CleberMesScanResult> SendScanAsync(
            RunMode runMode,
            string pid,
            string workOrder,
            string partNo,
            string gmesResult,
            string gmesMessage);

        Task<CleberMesScanResult> SendRescanOnlyAsync(string pid);

        Task<CleberMesScanResult> SendTrayCompleteAsync(RunMode runMode, string trayId);
    }

    public sealed class CleberMesScanResult
    {
        public bool Success { get; init; }
        public bool Ready { get; init; }
        public bool Completed { get; init; }
        public bool PartChanged { get; init; }
        public bool LotProcessed { get; init; }
        public bool TrayLoaded { get; init; }
        public string ResultCode { get; init; } = string.Empty;
        public string MesResultCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string PID { get; init; } = string.Empty;
        public string TrayId { get; init; } = string.Empty;
        public int CurrentQty { get; init; }
        public int MaxQty { get; init; }
        public string PlantCode { get; init; } = string.Empty;
        public string LineCode { get; init; } = string.Empty;
        public string OperationCode { get; init; } = string.Empty;
        public string LaneCode { get; init; } = string.Empty;

        public bool IsReadyStatus => Success && Ready && ResultCode.Equals("READY", System.StringComparison.OrdinalIgnoreCase);
        public bool IsStatusResult => Type.Equals("STATUS_RESULT", System.StringComparison.OrdinalIgnoreCase);
        public bool IsOk => Success && ResultCode.Equals("OK", System.StringComparison.OrdinalIgnoreCase);
        public bool IsCompletedOk => Success && Completed && ResultCode.Equals("COMPLETED", System.StringComparison.OrdinalIgnoreCase);
        public bool IsPartChanged => PartChanged && ResultCode.Equals("PART_NO_CHANGED", System.StringComparison.OrdinalIgnoreCase);
        public bool IsSuccessfulScan => IsOk || IsCompletedOk;
    }
}
