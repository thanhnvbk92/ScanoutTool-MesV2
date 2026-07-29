using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Models
{
    public interface IAppState
    {
        bool IsRunning { get; set; }
    }

    public partial class AppState : ObservableObject, IAppState
    {
        [ObservableProperty] private bool isRunning;
    }
}
