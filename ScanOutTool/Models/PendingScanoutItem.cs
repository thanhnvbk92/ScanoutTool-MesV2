using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace ScanOutTool.Models
{
    public partial class PendingScanoutItem : ObservableObject
    {
        [ObservableProperty] private string pID = string.Empty;
        [ObservableProperty] private string workOrder = string.Empty;
        [ObservableProperty] private string partNo = string.Empty;
        [ObservableProperty] private string errorMessage = string.Empty;
        [ObservableProperty] private DateTime failedTime = DateTime.Now;
        [ObservableProperty] private DateTime? lastRetryTime;
        [ObservableProperty] private int retryCount;

        public string FailedTimeFormatted => FailedTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string LastRetryTimeFormatted => LastRetryTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

        partial void OnFailedTimeChanged(DateTime value)
        {
            OnPropertyChanged(nameof(FailedTimeFormatted));
        }

        partial void OnLastRetryTimeChanged(DateTime? value)
        {
            OnPropertyChanged(nameof(LastRetryTimeFormatted));
        }
    }
}
