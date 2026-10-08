namespace ScanOutTool.Services
{
    public interface IScanDataService
    {
        (bool IsBlocked, string Reason) IsBlocked(string pid);
    }
}
