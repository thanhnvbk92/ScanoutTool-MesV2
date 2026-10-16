#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScanOutTool.Models
{
    public sealed class AvPidInoutDto
    {
        public string Pid { get; set; } = string.Empty;
        public int? MachineId { get; set; }
        public string? MachineName { get; set; }
        public DateTime? InputTime { get; set; }
        public DateTime? OutputTime { get; set; }
        public int? SlotNo { get; set; }
        public int? TrayNo { get; set; }
        public string? PackNo { get; set; }
        public int? PackStatus { get; set; }
        public string? Note { get; set; }
        public string? Wo { get; set; }
        public string? Ebr { get; set; }
        public int? ClassNo { get; set; }
    }

    public sealed class PushResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public JsonElement? Data { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
