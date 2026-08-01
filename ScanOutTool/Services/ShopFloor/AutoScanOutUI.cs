using ScanOutTool.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using UIAutoLib.Services;

namespace ScanOutTool.Services
{
    public class AutoScanOutUI : IAutoScanOutUI
    {
        private string processName = "LGE.SFC.MainFrame";
        private string pidTextBoxName = "txtSerialNo";
        private string ebrTextBoxName = "txtModelSuffix";
        private string woTextBoxName = "txtWorkOrder";
        private string resultTextBoxName = "txtResult";
        private string messageTextBoxName = "txtMessage";
        private string progressTextName = "txtProgress";
        private string processIDTextName = "txtPROCID";
        private string clearButtonName = "btnClear";
        private string traceButtonName = "btnLotHistory";
        private string lotHistoryWindowTitle = "LOT History";

        private AutomationService automation;
        private AutomationService pendingAutomation;
        private bool isAttached = false;
        private string oldProgessbarString = "";
        private string oldMessage = "";
        private string oldResult = "";
        private string oldPID = "";

        private event Action<string> pIDChanged;

        private System.Timers.Timer _timer;

        private readonly ILoggingService _loggingService;



        public AutoScanOutUI(ILoggingService loggingService)
            : this(loggingService, TimeSpan.FromSeconds(30))
        {
        }

        public AutoScanOutUI(ILoggingService loggingService, TimeSpan attachTimeout)
        {
            _loggingService = loggingService;
            isAttached = AttachWhenReady(attachTimeout);
            if (!isAttached)
            {
                _loggingService.LogWarning($"Could not attach to ready ShopFloor UI. Please check App {processName} and open the ScanOut screen.");
            }
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += TimerElapsed;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private bool AttachWhenReady(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            Exception lastException = null;
            int attempt = 0;

            while (sw.Elapsed < timeout)
            {
                attempt++;
                try
                {
                    _loggingService.LogInformation($"Attaching to ShopFloor UI, attempt {attempt}...");
                    if (TryAttachToScanoutWindow(attempt))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _loggingService.LogWarning($"ShopFloor attach attempt {attempt} failed: {ex.Message}");
                }

                Thread.Sleep(1000);
            }

            if (lastException != null)
            {
                _loggingService.LogWarning($"Last ShopFloor attach error: {lastException.Message}");
            }

            return false;
        }

        private bool TryAttachToScanoutWindow(int attempt)
        {
            var processes = Process.GetProcessesByName(processName)
                .OrderByDescending(HasMainWindow)
                .ThenByDescending(SafeGetStartTime)
                .ToArray();

            if (processes.Length == 0)
            {
                _loggingService.LogInformation($"ShopFloor process {processName} is not running yet.");
                return false;
            }

            foreach (var process in processes)
            {
                try
                {
                    var candidateAutomation = GetOrAttachCandidateAutomation(process.Id);
                    if (candidateAutomation == null)
                    {
                        _loggingService.LogInformation($"ShopFloor process {process.Id} is running but main UI is not ready.");
                        continue;
                    }

                    string currentScreen = candidateAutomation.ReadTextByAutomationId(processIDTextName);
                    if (IsScanoutProcessId(currentScreen))
                    {
                        Attach(candidateAutomation, currentScreen);
                        return true;
                    }

                    _loggingService.LogInformation($"ShopFloor process {process.Id} is open but ScanOut screen is not ready. Current screen: {currentScreen}");

                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Failed to inspect ShopFloor process {process.Id}: {ex.Message}");
                }
            }

            return false;
        }

        private AutomationService GetOrAttachCandidateAutomation(int processId)
        {
            if (pendingAutomation != null && pendingAutomation.ProcessId == processId)
            {
                pendingAutomation.ClearCache();
                return pendingAutomation;
            }

            var candidateAutomation = new AutomationService();
            if (!candidateAutomation.AttachToProcess(processId))
            {
                return null;
            }

            pendingAutomation = candidateAutomation;
            return pendingAutomation;
        }

        private void Attach(AutomationService automationService, string processIdText)
        {
            automation = automationService;
            pendingAutomation = null;
            isAttached = true;
            _loggingService.LogInformation($"Attached to ready ShopFloor UI with process id {automation.ProcessId}, screen {processIdText}.");
        }

        private static bool HasMainWindow(Process process)
        {
            try
            {
                return process.MainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static DateTime SafeGetStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private int? GetProcessIdByName(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            return processes.FirstOrDefault()?.Id;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (automation == null)
            {
                isAttached = TryAttachToScanoutWindow(attempt: 1);
                return;
            }

            bool attachedProcessStillRunning = Process.GetProcessesByName(processName)
                .Any(process => process.Id == automation.ProcessId);
            //_loggingService.LogInformation($"IsScanOut: {IsScanoutUI()}");
            //_loggingService.LogInformation($"IsHaveDialog: {automation.IsHaveDialogWindow()}");

            if (!attachedProcessStillRunning)
            {
                _loggingService.LogInformation($"Attached ShopFloor process {automation.ProcessId} is no longer running.");

                isAttached = TryAttachToScanoutWindow(attempt: 3);
                if (!isAttached)
                {
                    _loggingService.LogError($"Can not attach to ScanOut screen in process {processName}." );
                }
                else
                {
                    _loggingService.LogInformation($"Attached to process {processName} with id {automation.ProcessId}");
                }
            }
        }

        public string ReadMainWindowTitle()
        {
            if (isAttached)
            {
                return automation.GetMainWindowTitle();
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadPID()
        {
            if (isAttached)
            {
                string pid = automation.ReadTextByAutomationId(pidTextBoxName);
                if (pid != oldPID)

                {
                    oldPID = pid;
                    pIDChanged?.Invoke(pid);
                }
                return pid;
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadEBR()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(ebrTextBoxName).TrimEnd('.');
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadWO()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(woTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadResult()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(resultTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadMessage()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(messageTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public string ReadProgress()
        {
            if (isAttached)
            {
                return automation.ReadTextByAutomationId(progressTextName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public (double X, double Y, double Width, double Height) GetResultElementBounds()
        {
            if (isAttached)
            {
                return automation.GetElementBounds(resultTextBoxName);
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        public bool ClickClear()
        {
            EnsureAttached();
            return automation.ClickByAutomationIdPreserveForeground(clearButtonName);
        }

        public bool ClickTrace()
        {
            EnsureAttached();
            if (automation.ShowWindowByTitle(lotHistoryWindowTitle))
            {
                _loggingService.LogInformation("LOT History window is already open. Brought it to front.");
                return true;
            }

            if (!automation.ClickByAutomationIdPreserveForeground(traceButtonName))
            {
                return false;
            }

            return WaitAndShowLotHistoryWindow(TimeSpan.FromSeconds(2));
        }

        private bool WaitAndShowLotHistoryWindow(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (automation.ShowWindowByTitle(lotHistoryWindowTitle))
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return true;
        }

        public IntPtr GetMainHandle()
        {
            if (isAttached)
            {
                return automation.GetMainHandle();
            }
            else
            {
                throw new Exception("Failed to attach to the process.");
            }
        }

        private async Task<bool> WaitForGUIChangeAsync(int timeout = 5000)
        {
            int elapsed = 0;
            while (elapsed < timeout)
            {
                string currentPID = ReadPID();
                string currentProgress = automation.ReadTextByAutomationId(progressTextName);
                string currentMessage = ReadMessage();
                string currentResult = ReadResult();
                if (currentPID != oldPID)
                {
                    oldPID = currentPID;
                    return true;
                }
                if (currentProgress != oldProgessbarString)
                {
                    oldProgessbarString = currentProgress;
                    return true;
                }
                if (currentMessage != oldMessage)
                {
                    oldMessage = currentMessage;
                    return true;
                }
                await Task.Delay(100);
                elapsed += 100;
            }
            return false;
        }

        public async Task<PCB> ReadPCBInfor()
        {
            bool isChanged = await WaitForGUIChangeAsync();
            if (!isChanged)
            {
                return null;
            }
            PCB pCB = new PCB();
            pCB.PID = ReadPID();
            pCB.EBR = ReadEBR();
            pCB.WO = ReadWO();
            pCB.Result = ReadResult();
            pCB.Message = ReadMessage();
            return pCB;
        }

        public bool IsScanoutUI()
        {
            if (!isAttached || automation == null)
            {
                return false;
            }

            return IsScanoutProcessId(automation.ReadTextByAutomationId(processIDTextName));
        }

        private void EnsureAttached()
        {
            if (!isAttached || automation == null)
            {
                throw new InvalidOperationException("ShopFloor UI is not attached.");
            }
        }

        private static bool IsScanoutProcessId(string processId)
        {
            return !string.IsNullOrWhiteSpace(processId)
                && processId.Contains("PGZM5000", StringComparison.OrdinalIgnoreCase);
        }
    }
}
