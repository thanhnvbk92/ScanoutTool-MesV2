using ScanOutTool.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class BlockRFService:IBlockRFService
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggingService _loggingService;
        private List<RFInfo> _rFInfos = new List<RFInfo>();

        public BlockRFService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _loggingService.LogInformation("Khởi tạo dịch vụ BlockRFService...");
            _httpClient = new HttpClient() {Timeout =TimeSpan.FromSeconds(1) };
        }

        public async Task<RFInfo> IsBlock(string pid)
        {
            _rFInfos.Clear();   

            _loggingService.LogInformation($"Checking if PID {pid} is blocked...");
            // Simulate a delay for the blocking check
            await Task.Delay(100);
            string jsonString = await GetJsonAsync(@$"http://10.221.191.183:8081/api/TraceBackHistory/getErorrLogByPid/{pid}");
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            if (!string.IsNullOrEmpty(jsonString))
            {
                try
                {
                    _rFInfos = JsonSerializer.Deserialize<List<RFInfo>>(jsonString, options) ?? new List<RFInfo>();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Lỗi khi giải mã JSON RFInfo: {ex.Message}");
                    _rFInfos = new List<RFInfo>();
                }
            }
            
            var activeBlocks = _rFInfos.Where(x => !x.Cleared).ToList();

            if (activeBlocks.Count == 0)
            {        
                _loggingService.LogInformation($"No blocked RF information found for PID {pid}.");
                return null;
            }         
            else
            {
                var blockingItem = activeBlocks.FirstOrDefault(x =>
                {
                    string ant = (x.AntNumber ?? "").Trim().ToUpper();
                    bool isExemptAntenna = (ant != "A1" && ant != "B5");
                    bool isExemptValue = Math.Abs(x.ValueNg) > 500;

                    if (isExemptValue)
                    {
                        _loggingService.LogInformation($"RF block for PID {pid} (Ant: {x.AntNumber}, Value: {x.ValueNg}) is exempt because Value > 500.");
                        return false;
                    }
                    return true;
                });

                if (blockingItem == null)
                {
                    _loggingService.LogInformation($"All active RF blocks for PID {pid} are exempt => No block.");
                    return null;
                }

                _loggingService.LogInformation($"Found blocking RF information for PID {pid}: Band {blockingItem.Band}, Machine {blockingItem.MachineIP}, AntNumber {blockingItem.AntNumber}, Value {blockingItem.ValueNg}.");
                return blockingItem;
            }    
        }


        private async Task<string> GetJsonAsync(string url)
        {
            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                _loggingService.LogInformation($"Dữ liệu JSON từ URL {url}: {json}");
                return json;
            }
            catch (Exception ex)
            {
                // Có thể log lỗi hoặc throw lại nếu cần
                _loggingService.LogError ($"Lỗi khi lấy dữ liệu từ URL: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
