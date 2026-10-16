using Oracle.ManagedDataAccess.Client;
using ScanOutTool.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class ScanDataService : IScanDataService
    {
        private const string DbPort = "1521";
        private const string DbName = "HSEVNPDB";
        private const string DbUsername = "INFINITY21_JSMES";
        private const string DbPassword = "INFINITY21_JSMES";
        private const string TraceBlockEndpoint = "/api/QualityApi/DataApi/block/trace-block";
        private const string AvPidOutputEndpoint = "/api/PcbaApi/output/av-pid-inout";
        private readonly IConfigService _configService;
        private readonly ILoggingService _loggingService;
        private readonly HttpClient _httpClient;
        private readonly HttpClient _pushHttpClient;

        public ScanDataService(IConfigService configService, ILoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            _pushHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        public async Task<PushResultDto> PushAvPidOutputAsync(AvPidInoutDto item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Pid))
            {
                throw new ArgumentException("QA output data requires PID.", nameof(item));
            }

            string server = _configService.Config.QaBlockApiServer?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(server))
            {
                throw new InvalidOperationException("QA API server is not configured.");
            }

            string url = BuildQaApiUrl(server, AvPidOutputEndpoint);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                _loggingService.LogInformation(
                    $"[QA Output] Pushing PID={item.Pid}, MachineId={item.MachineId}, MachineIP={item.MachineName}, " +
                    $"SlotNo={item.SlotNo}, PackNo={item.PackNo}, WO={item.Wo}, EBR={item.Ebr}, " +
                    $"OutputTime={item.OutputTime:O} to {url}");

                using HttpResponseMessage response = await _pushHttpClient.PostAsJsonAsync(url, item);
                response.EnsureSuccessStatusCode();

                PushResultDto result = await response.Content.ReadFromJsonAsync<PushResultDto>();
                result ??= new PushResultDto { Success = true };
                _loggingService.LogInformation(
                    $"[QA Output] Push completed for PID={item.Pid}. Success={result.Success}, Message={result.Message}");
                return result;
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] QA output push: PID={item.Pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            }
        }

        public async Task<(bool IsBlocked, string Reason)> IsBlockedAsync(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                return (false, string.Empty);
            }

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                Task<(bool IsBlocked, string Reason)> apiCheckTask = CheckQaApiBlockAsync(pid);
                (bool IsBlocked, string Reason) legacyResult = CheckLegacyOracleBlock(pid);
                (bool IsBlocked, string Reason) apiResult = await apiCheckTask;

                if (legacyResult.IsBlocked && apiResult.IsBlocked)
                {
                    string combinedReason = $"[Oracle] {legacyResult.Reason} | [QA API] {apiResult.Reason}";
                    _loggingService.LogWarning($"[QA Block] PID {pid} blocked by both legacy Oracle and QA API. Reason={combinedReason}");
                    return (true, combinedReason);
                }

                if (legacyResult.IsBlocked)
                {
                    return legacyResult;
                }

                return apiResult;
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] QA combined block check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            }
        }

        private (bool IsBlocked, string Reason) CheckLegacyOracleBlock(string pid)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                _loggingService.LogInformation($"[QA Legacy] Checking PID {pid} in Oracle TBL_BLOCK.");
                using OracleConnection connection = new(GetConnectionString());
                connection.Open();

                using OracleCommand command = new()
                {
                    Connection = connection,
                    CommandText = "SELECT REASON FROM TBL_BLOCK WHERE PID = :pid AND STATUS = 0",
                    CommandType = CommandType.Text
                };

                command.Parameters.Add("pid", OracleDbType.Varchar2).Value = pid;

                using OracleDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    string reason = reader["REASON"]?.ToString() ?? string.Empty;
                    _loggingService.LogWarning($"[QA Legacy] PID {pid} blocked in Oracle. Reason={reason}");
                    return (true, reason);
                }

                _loggingService.LogInformation($"[QA Legacy] PID {pid} is not blocked in Oracle.");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[QA Legacy] Oracle check failed for PID {pid}: {ex.Message}");
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] QA legacy Oracle check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            }

            return (false, string.Empty);
        }

        private async Task<(bool IsBlocked, string Reason)> CheckQaApiBlockAsync(string pid)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string server = _configService.Config.QaBlockApiServer?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(server))
            {
                _loggingService.LogInformation("[QA Block] API server is not configured. Skip QA block check.");
                _loggingService.LogInformation($"[Timing] QA API check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
                return (false, string.Empty);
            }

            string url = $"{BuildQaApiUrl(server, TraceBlockEndpoint)}?pid={Uri.EscapeDataString(pid.Trim())}&traceMethod=PID";
            try
            {
                _loggingService.LogInformation($"[QA Block] Checking PID {pid}: {url}");
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                QaBlockResponse result = JsonSerializer.Deserialize<QaBlockResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null || !result.Success)
                {
                    _loggingService.LogWarning($"[QA Block] API returned unsuccessful response for PID {pid}.");
                    return (false, string.Empty);
                }

                List<QaBlockItem> blocks = result.Data ?? new List<QaBlockItem>();
                if (blocks.Count == 0)
                {
                    _loggingService.LogInformation($"[QA Block] PID {pid} is not blocked. Records=0.");
                    return (false, string.Empty);
                }

                QaBlockItem block = blocks.First();
                string reason = string.IsNullOrWhiteSpace(block.Reason)
                    ? $"Status={block.Status}"
                    : block.Reason.Trim();

                _loggingService.LogWarning(
                    $"[QA Block] PID {pid} blocked. Records={blocks.Count}, Status={block.Status}, " +
                    $"User={block.Username}, Time={block.Time}, Reason={reason}");
                return (true, reason);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[QA Block] Check failed for PID {pid}: {ex.Message}");
                return (false, string.Empty);
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] QA API check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            }
        }

        private string GetConnectionString()
        {
            string dbHost = string.IsNullOrWhiteSpace(_configService.Config.ServerIP)
                ? "10.7.10.56"
                : _configService.Config.ServerIP;

            return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={dbHost})(PORT={DbPort}))" +
                   $"(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={DbName})));" +
                   $"User Id={DbUsername};Password={DbPassword};";
        }

        private static string BuildQaApiUrl(string server, string endpoint)
        {
            string baseUrl = server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? server.TrimEnd('/')
                : $"http://{server.TrimEnd('/')}";

            int queryIndex = baseUrl.IndexOf('?');
            if (queryIndex >= 0)
            {
                baseUrl = baseUrl.Substring(0, queryIndex).TrimEnd('/');
            }

            if (baseUrl.EndsWith(TraceBlockEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - TraceBlockEndpoint.Length).TrimEnd('/');
            }

            if (baseUrl.EndsWith(AvPidOutputEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - AvPidOutputEndpoint.Length).TrimEnd('/');
            }

            return $"{baseUrl}/{endpoint.TrimStart('/')}";
        }

        private sealed class QaBlockResponse
        {
            public bool Success { get; set; }
            public List<QaBlockItem> Data { get; set; } = new();
        }

        private sealed class QaBlockItem
        {
            public string Time { get; set; } = string.Empty;
            public string Ebr { get; set; } = string.Empty;
            public string WorkOder { get; set; } = string.Empty;
            public string Pid { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}
