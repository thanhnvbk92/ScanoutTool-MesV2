using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Runtime.Serialization;

namespace ScanOutTool.ViewModels.Controls
{
    [DataContract]
    public partial class SerialPortSettingViewModel : ObservableObject
    {
        [DataMember]
        [ObservableProperty]
        private string selectedPort;

        [DataMember]
        [ObservableProperty]
        private int selectedBaudRate;

        [DataMember]
        [ObservableProperty]
        private Parity selectedParity;

        [DataMember]
        [ObservableProperty]
        private StopBits selectedStopBits;

        [DataMember]
        [ObservableProperty]
        private int selectedDataBits;

        public ObservableCollection<string> AvailablePorts { get; } = new(SerialPort.GetPortNames());
        public ObservableCollection<int> BaudRates { get; } = new() { 9600, 19200, 38400, 57600, 115200 };
        public ObservableCollection<Parity> Parities { get; } = new((Parity[])System.Enum.GetValues(typeof(Parity)));
        public ObservableCollection<StopBits> StopBitOptions { get; } = new((StopBits[])System.Enum.GetValues(typeof(StopBits)));
        public ObservableCollection<int> DataBitsOptions { get; } = new() { 5, 6, 7, 8 };

        public SerialPortSettingViewModel()
        {
            SelectedBaudRate = 9600;
            SelectedParity = Parity.None;
            SelectedStopBits = StopBits.One;
            SelectedDataBits = 8;
        }
    }
}
