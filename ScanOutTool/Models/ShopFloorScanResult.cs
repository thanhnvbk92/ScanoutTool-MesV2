namespace ScanOutTool.Models
{
    public class ShopFloorScanResult
    {
        public string PID { get; set; } = string.Empty;
        public string PartNo { get; set; } = string.Empty;
        public string WorkOrder { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Progress { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsOk => Result == "OK";
    }
}
