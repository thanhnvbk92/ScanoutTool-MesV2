using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace ScanOutTool.Models
{
    public partial class SavedMagazineItem : ObservableObject
    {
        [ObservableProperty] private string magazineNo = string.Empty;
        [ObservableProperty] private int qty;
        [ObservableProperty] private int qpm;
        [ObservableProperty] private DateTime savedTime = DateTime.Now;

        public string SavedTimeFormatted => SavedTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
