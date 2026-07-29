using System;

namespace ScanOutTool.Services
{
    public interface ILoggingService
    {
        public event EventHandler<LogEntry>? OnNewLog;
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message);
    }
}
