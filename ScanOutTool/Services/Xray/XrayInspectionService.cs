using ScanOutTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public sealed class XrayInspectionService : IXrayInspectionService
    {
        private readonly IConfigService _configService;
        private readonly ILoggingService _loggingService;
        private readonly HttpClient _httpClient;

        public XrayInspectionService(IConfigService configService, ILoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        }

        public async Task<XrayTraceResult> GetBlockingResultAsync(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                return null;
            }

            string server = _configService.Config.XrayApiServer?.Trim() ?? string.Empty;
            string port = _configService.Config.XrayApiPort?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(server))
            {
                _loggingService.LogInformation("[Xray] API server is not configured. Skip Xray check.");
                return null;
            }

            string url = BuildSearchUrl(server, port, pid);
            try
            {
                _loggingService.LogInformation($"[Xray] Checking PID {pid}: {url}");
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    _loggingService.LogInformation($"[Xray] No data for PID {pid}.");
                    return null;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                List<XrayTraceResult> results = JsonSerializer.Deserialize<List<XrayTraceResult>>(json, options)
                    ?? new List<XrayTraceResult>();

                XrayTraceResult blockingResult = results.FirstOrDefault(x =>
                    string.Equals(x.FinalResult?.Trim(), "NG", StringComparison.OrdinalIgnoreCase));

                if (blockingResult == null)
                {
                    _loggingService.LogInformation($"[Xray] PID {pid} is not blocked. Records={results.Count}.");
                    return null;
                }

                _loggingService.LogWarning($"[Xray] PID {pid} blocked. FinalResult={blockingResult.FinalResult}, Machine={blockingResult.MachineResult}, AI={blockingResult.AiResult}, User={blockingResult.UserResult}.");
                return blockingResult;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Xray] Check failed for PID {pid}: {ex.Message}");
                return null;
            }
        }

        private static string BuildSearchUrl(string server, string port, string pid)
        {
            string baseUrl = server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? server.TrimEnd('/')
                : $"http://{server.TrimEnd('/')}";

            int authorityStart = baseUrl.IndexOf("://", StringComparison.Ordinal) + 3;
            string authority = authorityStart >= 3 ? baseUrl.Substring(authorityStart) : baseUrl;
            if (!string.IsNullOrWhiteSpace(port) && !authority.Contains(":"))
            {
                baseUrl = $"{baseUrl}:{port}";
            }

            return $"{baseUrl}/api/pcbs/trace/search?pid={Uri.EscapeDataString(pid.Trim())}";
        }
    }
}
