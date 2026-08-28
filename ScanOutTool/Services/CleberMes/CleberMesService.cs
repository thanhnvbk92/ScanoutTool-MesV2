using HSSMES.Cleber.MES.Client.IVIPipe;
using ScanOutTool.Models;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public sealed class CleberMesService : ICleberMesService
    {
        private readonly ICleberMesPipeClient _client;

        public CleberMesService()
            : this(new CleberMesPipeClient())
        {
        }

        public CleberMesService(ICleberMesPipeClient client)
        {
            _client = client;
        }

        public async Task<CleberMesScanResult> GetStatusAsync()
        {
            IVIScanOutPipeResponse response = await _client.GetStatusAsync();
            return Map(response);
        }

        public async Task<CleberMesScanResult> SendScanAsync(
            RunMode runMode,
            string pid,
            string workOrder,
            string partNo,
            string gmesResult,
            string gmesMessage)
        {
            IVIScanOutPipeRequest request = new()
            {
                Version = "1.0",
                Type = "SCAN",
                RunMode = ToCleberRunMode(runMode),
                MessageId = Guid.NewGuid().ToString("N"),
                PID = pid?.Trim() ?? string.Empty,
                ScanTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")
            };

            if (runMode == RunMode.ScanOut_Rescan)
            {
                request.WorkOrder = workOrder?.Trim() ?? string.Empty;
                request.PartNo = partNo?.Trim() ?? string.Empty;
                request.GmesResult = gmesResult?.Trim() ?? string.Empty;
                request.GmesMessage = gmesMessage?.Trim() ?? string.Empty;
            }
            else if (runMode == RunMode.ScanOutOnly)
            {
                request.WorkOrder = workOrder?.Trim() ?? string.Empty;
                request.PartNo = partNo?.Trim() ?? string.Empty;
            }

            IVIScanOutPipeResponse response = await _client.SendScanAsync(request);
            return Map(response);
        }

        public async Task<CleberMesScanResult> SendRescanOnlyAsync(string pid)
        {
            IVIScanOutPipeRequest request = new()
            {
                Version = "1.0",
                Type = "SCAN",
                RunMode = IVIScanOutPipeConstants.RunModeRescanOnly,
                MessageId = Guid.NewGuid().ToString("N"),
                PID = pid?.Trim() ?? string.Empty,
                ScanTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")
            };

            IVIScanOutPipeResponse response = await _client.SendScanAsync(request);
            return Map(response);
        }

        private static string ToCleberRunMode(RunMode runMode)
        {
            return runMode switch
            {
                RunMode.ScanOut_Rescan => IVIScanOutPipeConstants.RunModeScanOutRescan,
                RunMode.RescanOnly => IVIScanOutPipeConstants.RunModeRescanOnly,
                RunMode.ScanOutOnly => IVIScanOutPipeConstants.RunModeScanOutOnly,
                _ => IVIScanOutPipeConstants.RunModeScanOutOnly
            };
        }

        private static CleberMesScanResult Map(IVIScanOutPipeResponse response)
        {
            return new CleberMesScanResult
            {
                Success = response.Success,
                Ready = response.Ready,
                Completed = response.Completed,
                PartChanged = response.PartChanged,
                ResultCode = response.ResultCode ?? string.Empty,
                Message = response.Message ?? string.Empty,
                PID = response.PID ?? string.Empty,
                TrayId = response.TrayId ?? string.Empty,
                CurrentQty = response.CurrentQty,
                MaxQty = response.MaxQty,
                PlantCode = response.PlantCode ?? string.Empty,
                LineCode = response.LineCode ?? string.Empty,
                OperationCode = response.OperationCode ?? string.Empty,
                LaneCode = response.LaneCode ?? string.Empty
            };
        }
    }
}
