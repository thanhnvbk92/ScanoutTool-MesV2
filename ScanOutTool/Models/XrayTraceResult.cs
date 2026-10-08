using System;
using System.Text.Json.Serialization;

namespace ScanOutTool.Models
{
    public sealed class XrayTraceResult
    {
        [JsonPropertyName("pid")]
        public string PID { get; set; } = string.Empty;

        [JsonPropertyName("machine_result")]
        public string MachineResult { get; set; } = string.Empty;

        [JsonPropertyName("ai_result")]
        public string AiResult { get; set; } = string.Empty;

        [JsonPropertyName("user_result")]
        public string UserResult { get; set; } = string.Empty;

        [JsonPropertyName("final_result")]
        public string FinalResult { get; set; } = string.Empty;

        [JsonPropertyName("user_confirmed")]
        public bool UserConfirmed { get; set; }

        [JsonPropertyName("client_time")]
        public DateTime? ClientTime { get; set; }
    }
}
