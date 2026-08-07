using DataExcuter;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class ScanDataService : IScanDataService
    {
        private readonly DataExecuter _dataExecuter;

        public event EventHandler<string> StatusChanged;

        public bool ShouldClearAfterSend => _dataExecuter.ShouldClearAfterSend;

        public ScanDataService(IConfigService configService)
        {
            var cfg = configService.Config;
            _dataExecuter = new DataExecuter(new DataExecuterConfig
            {
                DbHost = cfg.ServerIP,
                WebIpAddress = cfg.ServerIP
            });

            _dataExecuter.StatusChanged += (sender, status) => StatusChanged?.Invoke(sender, status);
        }

        public Task<bool> StartSessionAsync()
        {
            return _dataExecuter.StartSessionAsync();
        }

        public (bool IsBlocked, string Reason) IsBlocked(string pid)
        {
            return _dataExecuter.IsBlocked(pid);
        }

        public Task<ProcessResult> ProcessDataAsync(string workOrder, string partNo, string pid, bool validateDatabase = true, bool transferToWeb = true)
        {
            return _dataExecuter.ProcessDataAsync(workOrder, partNo, pid, validateDatabase, transferToWeb);
        }

        public Task<int> GetPackQty()
        {
            return _dataExecuter.GetPackQty();
        }

        public Task<int> GetMagazineQty()
        {
            return _dataExecuter.GetMagazineQty();
        }

        public Task<RescanInfo> GetLastRescanInfo()
        {
            return _dataExecuter.GetLastRescanInfo();
        }

        public Task<bool> PrintManual()
        {
            return _dataExecuter.PrintManual();
        }

        public Task<bool> SaveLabelAsync()
        {
            return _dataExecuter.SaveLabelAsync();
        }

        public Task<bool> SearchMagazineNoAsync(string magazineNo)
        {
            return _dataExecuter.SearchMagazineNoAsync(magazineNo);
        }

        public Task<bool> SetTransactionTypeAsync(string transactionType)
        {
            return _dataExecuter.SetTransactionTypeAsync(transactionType);
        }
    }
}
