using ScanOutTool.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class ShopFloorService : IShopFloorService
    {
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;
        private IAutoScanOutUI? _autoScanOutUI;

        public ShopFloorService(ILoggingService loggingService, IConfigService configService)
        {
            _loggingService = loggingService;
            _configService = configService;
        }

        public async Task EnsureRunningAsync()
        {
            string exePath = _configService.Config.ShopFloorExePath;
            if (IsShopFloorRunning(exePath))
            {
                _loggingService.LogInformation("ShopFloor is already running.");
                return;
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                _loggingService.LogWarning("ShopFloor EXE path is not configured.");
                return;
            }

            if (!File.Exists(exePath))
            {
                _loggingService.LogError($"ShopFloor EXE path does not exist: {exePath}");
                return;
            }

            await StartShopFloorProcessAsync(exePath);
            await Task.Delay(1000);
        }

        public async Task InitializeAsync()
        {
            if (_autoScanOutUI != null)
            {
                try
                {
                    if (_autoScanOutUI.IsScanoutUI())
                    {
                        _loggingService.LogInformation("ShopFloor UI is already attached.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Existing ShopFloor UI attachment is not available: {ex.Message}");
                    _autoScanOutUI = null;
                }
            }

            await EnsureRunningAsync();
            _loggingService.LogInformation("Attaching to ShopFloor UI...");
            _autoScanOutUI = await RunWithTimeoutAsync(
                () => new AutoScanOutUI(_loggingService),
                TimeSpan.FromSeconds(45),
                "Attach to ShopFloor UI");

            if (_autoScanOutUI.IsScanoutUI())
            {
                _loggingService.LogInformation("ShopFloor UI attached successfully.");
            }
            else
            {
                _loggingService.LogWarning("ShopFloor UI is open but ScanOut screen is not ready.");
            }
        }

        public async Task RestartAsync()
        {
            string exePath = _configService.Config.ShopFloorExePath;
            _autoScanOutUI = null;

            foreach (Process process in GetShopFloorProcesses(exePath))
            {
                await StopProcessAsync(process);
            }

            await Task.Delay(500);
            await EnsureRunningAsync();
            _loggingService.LogInformation("Attaching to ShopFloor UI...");
            _autoScanOutUI = await RunWithTimeoutAsync(
                () => new AutoScanOutUI(_loggingService),
                TimeSpan.FromSeconds(45),
                "Attach to ShopFloor UI");

            if (_autoScanOutUI.IsScanoutUI())
            {
                _loggingService.LogInformation("ShopFloor UI attached successfully.");
            }
            else
            {
                _loggingService.LogWarning("ShopFloor UI is open but ScanOut screen is not ready.");
            }
        }

        public bool IsScanoutUI()
        {
            return _autoScanOutUI?.IsScanoutUI() ?? false;
        }

        public bool ClickClear()
        {
            EnsureInitialized();
            return _autoScanOutUI!.ClickClear();
        }

        public bool ClickTrace()
        {
            EnsureInitialized();
            return _autoScanOutUI!.ClickTrace();
        }

        public ShopFloorScanResult ReadCurrent()
        {
            EnsureInitialized();

            return new ShopFloorScanResult
            {
                PID = _autoScanOutUI!.ReadPID(),
                WorkOrder = _autoScanOutUI.ReadWO(),
                PartNo = _autoScanOutUI.ReadEBR(),
                Result = _autoScanOutUI.ReadResult(),
                Progress = _autoScanOutUI.ReadProgress(),
                Message = _autoScanOutUI.ReadMessage()
            };
        }

        public async Task<ShopFloorScanResult> WaitForResultAsync(string pid, int timeoutMs = 5000)
        {
            EnsureInitialized();

            pid = pid.Trim();
            var sw = Stopwatch.StartNew();

            while (pid != _autoScanOutUI!.ReadPID())
            {
                string currentPid = _autoScanOutUI.ReadPID();
                string message = _autoScanOutUI.ReadMessage();

                if (currentPid.Contains("PLZ Read below Message and Clear") && message.Contains(pid))
                {
                    return new ShopFloorScanResult
                    {
                        PID = pid,
                        Result = "NG",
                        Progress = _autoScanOutUI.ReadProgress(),
                        Message = message
                    };
                }

                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    _loggingService.LogError($"Timeout to get scanout PID {pid} result: {currentPid} - {message}:{sw.ElapsedMilliseconds}ms");
                    return new ShopFloorScanResult
                    {
                        PID = pid,
                        Result = "NG",
                        Progress = _autoScanOutUI.ReadProgress(),
                        Message = message
                    };
                }

                await Task.Delay(50);
            }

            return ReadCurrent();
        }

        private void EnsureInitialized()
        {
            if (_autoScanOutUI == null)
            {
                throw new InvalidOperationException("ShopFloor UI service is not initialized.");
            }
        }

        private static bool IsShopFloorRunning(string exePath)
        {
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                string expectedProcessName = Path.GetFileNameWithoutExtension(exePath);
                string expectedFullPath = Path.GetFullPath(exePath);

                return Process.GetProcesses()
                    .Any(process => IsExpectedProcess(process, expectedProcessName, expectedFullPath));
            }

            return IsShopFloorWindowOpen();
        }

        private static Process[] GetShopFloorProcesses(string exePath)
        {
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                string expectedProcessName = Path.GetFileNameWithoutExtension(exePath);
                string expectedFullPath = Path.GetFullPath(exePath);

                return Process.GetProcesses()
                    .Where(process => IsExpectedProcess(process, expectedProcessName, expectedFullPath))
                    .ToArray();
            }

            return Process.GetProcesses()
                .Where(process => HasMainWindowTitle(process, "GMES ShopFloorControl"))
                .ToArray();
        }

        private async Task StopProcessAsync(Process process)
        {
            try
            {
                _loggingService.LogInformation($"Stopping ShopFloor process: {process.ProcessName} ({process.Id})");

                if (!process.HasExited && process.CloseMainWindow())
                {
                    await Task.Run(() => process.WaitForExit(3000));
                }

                if (!process.HasExited)
                {
                    process.Kill();
                    await Task.Run(() => process.WaitForExit(5000));
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to stop ShopFloor process {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private async Task StartShopFloorProcessAsync(string exePath)
        {
            _loggingService.LogInformation($"Starting ShopFloor: {exePath}");

            Task<Process?> startTask = Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                    UseShellExecute = false
                };

                return Process.Start(startInfo);
            });

            Task completedTask = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completedTask != startTask)
            {
                _loggingService.LogWarning("Starting ShopFloor process did not return within 10s. Continuing to attach if the window is already open.");
                return;
            }

            Process? process = await startTask;
            if (process == null)
            {
                throw new InvalidOperationException("Process.Start returned null for ShopFloor.");
            }

            _loggingService.LogInformation($"ShopFloor process start requested. PID={process.Id}");
            process.Dispose();
        }

        private static async Task<T> RunWithTimeoutAsync<T>(Func<T> action, TimeSpan timeout, string operationName)
        {
            Task<T> task = Task.Run(action);
            Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));

            if (completedTask != task)
            {
                throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds:0}s.");
            }

            return await task;
        }

        private static bool IsShopFloorWindowOpen()
        {
            return Process.GetProcesses()
                .Any(process => HasMainWindowTitle(process, "GMES ShopFloorControl"));
        }

        private static bool IsExpectedProcess(Process process, string expectedProcessName, string expectedFullPath)
        {
            try
            {
                if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string processPath = process.MainModule?.FileName;
                return string.IsNullOrWhiteSpace(processPath)
                    || string.Equals(Path.GetFullPath(processPath), expectedFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static async Task WaitForShopFloorWindowAsync(int timeoutMs = 15000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (IsShopFloorWindowOpen())
                {
                    return;
                }

                await Task.Delay(500);
            }
        }

        private static bool HasMainWindowTitle(Process process, string titleContains)
        {
            try
            {
                return process.MainWindowHandle != IntPtr.Zero
                    && process.MainWindowTitle.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
