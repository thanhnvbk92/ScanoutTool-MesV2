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
        private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(30);
        private ShopFloorScanResult _lastResult = new ShopFloorScanResult();

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
            try
            {
                await AttachShopFloorUiAsync();
            }
            catch (TimeoutException ex)
            {
                _loggingService.LogWarning($"{ex.Message} Restarting ShopFloor and retrying attach once.");
                await RestartAsync();
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
            await AttachShopFloorUiAsync();
        }

        private async Task AttachShopFloorUiAsync()
        {
            _loggingService.LogInformation("Attaching to ShopFloor UI...");
            _autoScanOutUI = await RunWithTimeoutAsync(
                () => new AutoScanOutUI(_loggingService, AttachTimeout),
                AttachTimeout + TimeSpan.FromSeconds(2),
                "Attach to ShopFloor UI");

            if (_autoScanOutUI.IsScanoutUI())
            {
                _loggingService.LogInformation("ShopFloor UI attached successfully.");
            }
            else
            {
                _loggingService.LogWarning("ShopFloor UI is open but ScanOut screen is not ready.");
                throw new TimeoutException($"Attach to ShopFloor UI timed out after {AttachTimeout.TotalSeconds:0}s.");
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

            // 1. Capture initial ShopFloor state (lấy kết quả của lần đọc trước, mặc định rỗng khi mới khởi động)
            ShopFloorScanResult initialState = _lastResult;
            string initialPid = initialState.PID.Trim();
            string initialWO = initialState.WorkOrder.Trim();
            string initialPartNo = initialState.PartNo.Trim();
            string initialResult = initialState.Result.Trim();
            string initialMessage = initialState.Message.Trim();
            string initialProgress = initialState.Progress.Trim();

            _loggingService.LogInformation($"[ShopFloor] Waiting for PID update: Target='{pid}', Previous Scan PID='{initialPid}'");

            // 2. Phase 1: Wait until ShopFloor PID updates to match scanned target PID
            bool pidUpdatedOnShopFloor = false;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string currentPid = _autoScanOutUI!.ReadPID().Trim();
                string message = _autoScanOutUI.ReadMessage();

                if (currentPid.Contains("PLZ Read below Message and Clear") && message.Contains(pid))
                {
                    _loggingService.LogWarning($"[ShopFloor] Popup message detected for PID '{pid}': {message}");
                    _lastResult = new ShopFloorScanResult
                    {
                        PID = pid,
                        Result = "NG",
                        Progress = _autoScanOutUI.ReadProgress(),
                        Message = message
                    };
                    return _lastResult;
                }

                if (string.Equals(currentPid, pid, StringComparison.OrdinalIgnoreCase))
                {
                    pidUpdatedOnShopFloor = true;
                    break;
                }

                await Task.Delay(50);
            }

            if (!pidUpdatedOnShopFloor)
            {
                string currentPid = _autoScanOutUI!.ReadPID().Trim();
                string message = _autoScanOutUI.ReadMessage();
                _loggingService.LogError($"[ShopFloor] Timeout waiting for PID to update on UI. Expected: '{pid}', Found: '{currentPid}' - Message: '{message}' ({sw.ElapsedMilliseconds}ms)");
                _lastResult = new ShopFloorScanResult
                {
                    PID = pid,
                    Result = "NG",
                    Progress = _autoScanOutUI.ReadProgress(),
                    Message = $"Timeout: PID on ShopFloor ('{currentPid}') did not update to target PID ('{pid}')."
                };
                return _lastResult;
            }

            // 3. Phase 2: Once PID matches on ShopFloor, wait for ShopFloor response fields to update/stabilize
            _loggingService.LogInformation($"[ShopFloor] PID updated to '{pid}' on UI at {sw.ElapsedMilliseconds}ms. Verifying response data...");

            ShopFloorScanResult latestResult = ReadCurrent();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                latestResult = ReadCurrent();
                bool isResponseUpdated = false;

                bool pidMatches = string.Equals(latestResult.PID.Trim(), pid, StringComparison.OrdinalIgnoreCase);
                bool hasResult = !string.IsNullOrWhiteSpace(latestResult.Result);

                bool initialWasEmpty = string.IsNullOrWhiteSpace(initialPid) && string.IsNullOrWhiteSpace(initialResult);
                bool pidChanged = !string.Equals(initialPid, pid, StringComparison.OrdinalIgnoreCase);
                bool resultChanged = !string.Equals(latestResult.Result.Trim(), initialResult, StringComparison.OrdinalIgnoreCase);
                bool progressChanged = !string.Equals(latestResult.Progress.Trim(), initialProgress, StringComparison.OrdinalIgnoreCase);
                bool messageChanged = !string.Equals(latestResult.Message.Trim(), initialMessage, StringComparison.OrdinalIgnoreCase);

                // Dữ liệu sẵn sàng khi PID trên UI khớp với target PID và đã có Result ("OK"/"NG")
                if (pidMatches && hasResult && (initialWasEmpty || pidChanged || resultChanged || progressChanged || messageChanged))
                {
                    isResponseUpdated = true;
                }
                else if (pidMatches && hasResult)
                {
                    isResponseUpdated = true;
                }

                if (isResponseUpdated && !string.IsNullOrWhiteSpace(latestResult.Result))
                {
                    await Task.Delay(100);
                    ShopFloorScanResult stableResult = ReadCurrent();
                    if (string.Equals(stableResult.PID.Trim(), pid, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(stableResult.Result, latestResult.Result, StringComparison.OrdinalIgnoreCase))
                    {
                        _loggingService.LogInformation($"[ShopFloor] Verified PID '{pid}' response: Result='{stableResult.Result}', WO='{stableResult.WorkOrder}', PartNo='{stableResult.PartNo}' in {sw.ElapsedMilliseconds}ms");
                        _lastResult = stableResult;
                        return stableResult;
                    }
                }

                await Task.Delay(50);
            }

            _loggingService.LogWarning($"[ShopFloor] Response fields for PID '{pid}' did not change/stabilize before timeout ({sw.ElapsedMilliseconds}ms). Returning current state.");
            _lastResult = latestResult;
            return latestResult;
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

                return Process.GetProcessesByName(expectedProcessName)
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

                return Process.GetProcessesByName(expectedProcessName)
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
