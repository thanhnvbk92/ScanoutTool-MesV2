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
                _rFInfos = JsonSerializer.Deserialize<List<RFInfo>>(jsonString, options);
            }
            
            if(_rFInfos.Count==0)
            {        
                _loggingService.LogInformation($"No blocked RF information found for PID {pid}.");
                return null;
            }
            else
            {
                _loggingService.LogInformation($"Found {_rFInfos.Count} blocked RF information(s) for PID {pid} Band{_rFInfos[0].Band} Machine {_rFInfos[0].MachineIP}.");
                return _rFInfos[0];
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
