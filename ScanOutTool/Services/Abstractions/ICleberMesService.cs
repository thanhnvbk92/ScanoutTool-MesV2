using ScanOutTool.Models;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface ICleberMesService
    {
        Task<CleberMesScanResult> GetStatusAsync();

        Task<CleberMesScanResult> SendScanAsync(
            RunMode runMode,
            string pid,
            string workOrder,
            string partNo,
            string gmesResult,
            string gmesMessage);

        Task<CleberMesScanResult> SendRescanOnlyAsync(string pid);
    }

    public sealed class CleberMesScanResult
    {
        public bool Success { get; init; }
        public bool Ready { get; init; }
        public bool Completed { get; init; }
        public string ResultCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string PID { get; init; } = string.Empty;
        public string TrayId { get; init; } = string.Empty;
        public int CurrentQty { get; init; }
        public int MaxQty { get; init; }
        public string PlantCode { get; init; } = string.Empty;
        public string LineCode { get; init; } = string.Empty;
        public string OperationCode { get; init; } = string.Empty;
        public string LaneCode { get; init; } = string.Empty;

        public bool IsOk => Success && Ready && ResultCode.Equals("OK", System.StringComparison.OrdinalIgnoreCase);
    }
}
