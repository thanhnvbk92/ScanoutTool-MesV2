using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;

namespace ScanOutTool.Services
{
    public class LoggingService : ILoggingService
    {
        public ObservableCollection<LogEntry> LogEntries { get; }
        public event EventHandler<LogEntry>? OnNewLog;

        private string logPath = string.Empty;
        private DateTime currentDate = DateTime.Now.Date;
        private Timer? dailyCheckTimer;

        public LoggingService()
        {
            LogEntries = new ObservableCollection<LogEntry>();

            // Khởi tạo logger ban đầu
            ConfigureLogger();

            // Tạo timer kiểm tra sang ngày mới
            dailyCheckTimer = new Timer(CheckForNewDay, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Kiểm tra và cập nhật logger khi sang ngày mới.
        /// </summary>
        private void CheckForNewDay(object? state)
        {
            var now = DateTime.Now.Date;
            if (now != currentDate)
            {
                currentDate = now;
                ConfigureLogger();
                Log.Information("🎯 Đã chuyển sang ngày mới và tạo log mới.");
            }
        }

        /// <summary>
        /// Cấu hình logger cho ngày hiện tại.
        /// </summary>
        private void ConfigureLogger()
        {
            logPath = Path.Combine("logs", DateTime.Now.ToString("yyyy"), DateTime.Now.ToString("MM"));
            Directory.CreateDirectory(logPath);

            string logFileName = Path.Combine(logPath, $"log-.txt");

            Log.CloseAndFlush(); // Đóng logger cũ

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: logFileName,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            Log.Information("✅ Logger đã được cấu hình cho ngày: {Date}", currentDate.ToString("yyyy-MM-dd"));
        }

        public void LogInformation(string message)
        {
            Log.Information(message);
            AddLogToCollection("Info", message);
        }

        public void LogWarning(string message)
        {
            Log.Warning(message);
            AddLogToCollection("Warning", message);
        }

        public void LogError(string message)
        {
            Log.Error(message);
            AddLogToCollection("Error", message);
        }

        /// <summary>
        /// Thêm log vào ObservableCollection và kích hoạt sự kiện.
        /// </summary>
        private void AddLogToCollection(string level, string message)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Level = level,
                Message = message
            };
            LogEntries.Add(logEntry);
            OnNewLog?.Invoke(this, logEntry);
        }
    }

    public class LogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
