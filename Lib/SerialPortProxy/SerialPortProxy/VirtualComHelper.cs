using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.IO.Ports;

namespace SerialProxyLib
{
    public static class VirtualComHelper
    {
        private const string SetupcExe = @"C:\\Program Files (x86)\\com0com\\setupc.exe";

        public static void EnsureComPair(string comApp, string sniffCom, Action<string> logAction)
        {
            if (IsComPortVirtual(sniffCom))
            {
                if (!IsComPairExists(comApp, sniffCom))
                {
                    logAction($"{sniffCom} đã tồn tại nhưng không đúng cặp với {comApp}. Đang gỡ bỏ và tạo lại...");
                    RemoveComPort(sniffCom);
                    CreateComPair(comApp, sniffCom, logAction);
                }
                else
                {
                    logAction($"Cặp COM ảo {comApp} <-> {sniffCom} đã tồn tại.");
                }
            }
            else
            {
                logAction($"Tạo cặp COM ảo: {comApp} <-> {sniffCom}...");
                CreateComPair(comApp, sniffCom, logAction);
            }
        }

        public static string GetSniffCom(string comApp)
        {
            if (!comApp.ToUpper().StartsWith("COM"))
                throw new ArgumentException("Tên COM không hợp lệ", nameof(comApp));

            string numberPart = comApp.Substring(3);
            if (!int.TryParse(numberPart, out int comNumber))
                throw new ArgumentException("Không thể trích số từ tên COM", nameof(comApp));

            return $"COM{comNumber - 1}";
        }

        public static void CreateComPair(string comIn, string comOut, Action<string> logAction)
        {
            int pairIndex = 127; // Use a fixed or high index to avoid conflicts

            var startInfo = new ProcessStartInfo
            {
                FileName = SetupcExe,
                Arguments = $"install {pairIndex} PortName={comIn} PortName={comOut}",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(SetupcExe),
                Verb = "runas"
            };

            try
            {
                var proc = Process.Start(startInfo);
                proc?.WaitForExit();

                if (proc?.ExitCode != 0)
                    logAction($"[LỖI] setupc.exe trả về mã lỗi {proc?.ExitCode}");
                else
                    logAction($"[OK] Đã tạo cặp COM ảo: {comIn} <-> {comOut}");
            }
            catch (Exception ex)
            {
                logAction($"[LỖI] Không thể thực thi setupc.exe: {ex.Message}");
            }
        }

        public static void RemoveComPort(string comName)
        {
            try
            {
                Process.Start(SetupcExe, $"remove {comName}")?.WaitForExit();
            }
            catch { }
        }

        public static bool IsComPortVirtual(string comName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    string manufacturer = obj["Manufacturer"]?.ToString() ?? "";

                    if (name.Contains(comName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Contains("com0com", StringComparison.OrdinalIgnoreCase) ||
                            manufacturer.Contains("com0com", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool IsComPairExists(string comIn, string comOut)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = SetupcExe,
                    Arguments = "list",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(SetupcExe),
                    CreateNoWindow = true
                };
                var proc = Process.Start(startInfo);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                return output.Contains(comIn) && output.Contains(comOut);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsComExists(string com)
        {
            string[] Ports = SerialPort.GetPortNames();
            return Ports.Any(p => p.Equals(com, StringComparison.OrdinalIgnoreCase));
        }
    }
}
