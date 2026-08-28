using System;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SerialProxyLib
{
    public interface IProxyLogger
    {
        void Log(string message);
    }

    public class SerialDataEventArgs : EventArgs
    {
        public string Data { get; set; } = string.Empty;
    }

    public class SerialProxyManager
    {
        public event EventHandler<SerialDataEventArgs>? OnScannerDataReceived;
        public event EventHandler<SerialDataEventArgs>? OnAppDataReceived;
        public IProxyLogger? Logger { get; set; }

        private SerialPort? _devicePort; // Physical Scanner
        private SerialPort? _snifferPort; // Virtual port for App (the "other end" of Virtual COM)
        private bool _isRunning;
        private readonly StringBuilder _scannerBuffer = new();
        private readonly StringBuilder _appBuffer = new();

        public async Task StartProxy(string comDevice, string comApp, int baudRate = 9600, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, System.Threading.CancellationToken token = default)
        {
            try
            {
                string sniffCom = VirtualComHelper.GetSniffCom(comApp);
                VirtualComHelper.EnsureComPair(comApp, sniffCom, Log);

                _devicePort = new SerialPort(comDevice, baudRate, parity, dataBits, stopBits) { Encoding = Encoding.ASCII };
                _snifferPort = new SerialPort(sniffCom, baudRate, parity, dataBits, stopBits) { Encoding = Encoding.ASCII };

                _devicePort.DataReceived += (s, e) => HandleDataReceived(_devicePort, _scannerBuffer, OnScannerDataReceived);
                _snifferPort.DataReceived += (s, e) => HandleDataReceived(_snifferPort, _appBuffer, OnAppDataReceived);

                _devicePort.Open();
                _snifferPort.Open();
                _isRunning = true;

                Log($"Proxy running: {comDevice} (Scanner) <-> {comApp} (SFC via {sniffCom})");
                
                // Keep the task alive if requested, like the previous implementation
                try
                {
                    await Task.Delay(-1, token);
                }
                catch (TaskCanceledException) { }
                finally
                {
                    Stop();
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] StartProxy failed: {ex.Message}");
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            
            if (_devicePort != null)
            {
                if (_devicePort.IsOpen) _devicePort.Close();
                _devicePort.Dispose();
                _devicePort = null;
            }

            if (_snifferPort != null)
            {
                if (_snifferPort.IsOpen) _snifferPort.Close();
                _snifferPort.Dispose();
                _snifferPort = null;
            }

            Log("Proxy stopped.");
        }

        private void HandleDataReceived(SerialPort port, StringBuilder buffer, EventHandler<SerialDataEventArgs>? @event)
        {
            try
            {
                if (!port.IsOpen || port.BytesToRead == 0) return;

                // Đọc tất cả dữ liệu hiện có dưới dạng mảng byte để đảm bảo tính toàn vẹn
                byte[] bytes = new byte[port.BytesToRead];
                int bytesRead = port.Read(bytes, 0, bytes.Length);
                
                // Scanner terminates some scans with NUL bytes instead of CR/LF.
                string rawData = port.Encoding.GetString(bytes, 0, bytesRead);
                string data = ReferenceEquals(port, _devicePort)
                    ? Regex.Replace(rawData, @"(?:\0|#NUL)+", "\r")
                    : rawData.Replace("\0", "");
                lock (buffer)
                {
                    buffer.Append(data);

                    string content = buffer.ToString();
                    // Tìm kiếm các ký tự kết thúc dòng: \r hoặc \n
                    int separatorIdx;
                    while ((separatorIdx = content.IndexOfAny(new[] { '\r', '\n' })) >= 0)
                    {
                        string line = content.Substring(0, separatorIdx).Trim();
                        
                        // Cắt bỏ phần đã xử lý bao gồm cả ký tự xuống hàng
                        // (Xử lý trường hợp \r\n thì sẽ cắt tiếp \n ở vòng lặp sau)
                        buffer.Remove(0, separatorIdx + 1);
                        content = buffer.ToString();

                        if (!string.IsNullOrEmpty(line))
                        {
                            Log($"[RECEIVE] {port.PortName}: {line}");
                            @event?.Invoke(this, new SerialDataEventArgs { Data = line });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Read error on {port.PortName}: {ex.Message}");
            }
        }

        public async Task SendToScannerAsync(string data)
        {
            await Task.Run(() =>
            {
                if (_isRunning && _devicePort != null && _devicePort.IsOpen)
                {
                    _devicePort.Write(data);
                    Log($"[SENT TO SCANNER] {data.Trim()}");
                }
            });
        }

        public async Task SendToAppAsync(string data)
        {
            await Task.Run(() =>
            {
                if (_isRunning && _snifferPort != null && _snifferPort.IsOpen)
                {
                    _snifferPort.Write(data + "\r");
                    Log($"[SENT TO APP] {data.Trim()}");
                }
            });
        }

        private void Log(string message)
        {
            Logger?.Log(message);
        }
    }
}
