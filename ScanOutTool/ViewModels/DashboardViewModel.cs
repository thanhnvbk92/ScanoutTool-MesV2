using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutLogLib.Services;
using ScanOutTool.Models;
using ScanOutTool.Services;
using ScanOutTool.Views;
using ScanOutTool.ViewModels.Controls;
using SerialProxyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using System.IO;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Channels;

namespace ScanOutTool.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private enum QaBlockAction
        {
            AllowFullFlow,
            StopBeforeShopFloor
        }

        private sealed class QueuedScanItem
        {
            public string Data { get; init; } = string.Empty;
            public long EnqueuedTimestamp { get; init; }
        }

        private const string WorkAlreadyFinishedMessage = "Work already finished.";

        private readonly IAppState _appState;
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;
        private readonly IScanResultService _resultService;
        private readonly IBlockRFService _blockRFService;
        private readonly IAudioNotificationService _audioNotificationService;
        private readonly IShopFloorService _shopFloorService;
        private readonly IScanDataService _scanDataService;
        private readonly ICleberMesService _cleberMesService;
        private readonly IXrayInspectionService _xrayInspectionService;

        private SerialProxyManager _serialProxyManager;
        private bool _shouldClearShopFloorBeforeNextPid;
        private readonly System.Threading.SemaphoreSlim _scanProcessingLock = new(1, 1);
        private string _lastScannedPid = string.Empty;
        private DateTime _lastScannedTime = DateTime.MinValue;
        private const int DuplicateScanThresholdMs = 3000;
        private int _runModeValidationVersion;

        private readonly Channel<QueuedScanItem> _scanQueue = Channel.CreateUnbounded<QueuedScanItem>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private CancellationTokenSource? _queueCts;
        private Task? _queueConsumerTask;

        private bool IsScanOutOnly => SelectedRunMode == RunMode.ScanOutOnly;
        private bool IsRescanOnly => SelectedRunMode == RunMode.RescanOnly;
        private bool IsScanOutRescan => SelectedRunMode == RunMode.ScanOut_Rescan;
        private bool IsNone => SelectedRunMode == RunMode.None;
        private bool RequiresScanOutUi => RequiresScanOutUiMode(SelectedRunMode);

        [ObservableProperty] private bool isStarted;
        [ObservableProperty] private bool isSessionStarting;
        [ObservableProperty] private bool isStartButtonVisible = true;
        [ObservableProperty] private bool isInChooseEBRMode=false;
        [ObservableProperty] private bool isPCBtoSelectEBR = false;
        [ObservableProperty] private bool isConfirmEBRStep = false;
        [ObservableProperty] private bool isDataSending;

        partial void OnSelectedRunModeChanged(RunMode value)
        {
            if (_configService == null)
            {
                return;
            }

            _configService.Config.LastRunMode = value.ToString();
            _configService.Save();
            _loggingService?.LogInformation($"Saved last run mode: {value}");

            if (IsStarted && RequiresScanOutUiMode(value))
            {
                _ = ValidateScanOutModeAfterRunModeChangedAsync(value);
            }
        }

        private static bool RequiresScanOutUiMode(RunMode runMode)
        {
            return runMode == RunMode.ScanOutOnly || runMode == RunMode.ScanOut_Rescan;
        }

        private void UpdateRunModes()
        {
            RunModes = Enum.GetValues(typeof(RunMode)).Cast<RunMode>().ToList();
            if (!RunModes.Contains(SelectedRunMode))
            {
                SelectedRunMode = RunMode.ScanOut_Rescan;
            }
        }

        private RunMode LoadLastRunMode()
        {
            string savedRunMode = _configService.Config.LastRunMode;
            if (Enum.TryParse(savedRunMode, ignoreCase: true, out RunMode runMode))
            {
                return runMode;
            }

            return RunMode.ScanOut_Rescan;
        }
        [ObservableProperty] private string logs;
        [ObservableProperty] private string startBtnText;
        [ObservableProperty] private string pID;
        [ObservableProperty] private string partNo;
        [ObservableProperty] private string workOrder;
        [ObservableProperty] private string result;
        [ObservableProperty] private string progress;
        [ObservableProperty] private string resultMessage;
        [ObservableProperty] private RunMode selectedRunMode;
        [ObservableProperty] private List<RunMode> runModes;
        [ObservableProperty] private string selectedEBR;
        [ObservableProperty] private string informationMessage;
        [ObservableProperty] private bool isMessageOn =false;
        [ObservableProperty] private int magazineQty;
        [ObservableProperty] private string shopFloorExePath;
        [ObservableProperty] private string hmesResult;
        [ObservableProperty] private string hmesMessage;
        [ObservableProperty] private int hmesPackQty;
        [ObservableProperty] private int hmesMagazineQty;
        [ObservableProperty] private string hmesMagazineNo;
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<PendingScanoutItem> pendingScanouts = new();
        [ObservableProperty] private PendingScanoutItem selectedPendingScanout;
        [ObservableProperty] private bool isPendingScanoutRetrying;

        partial void OnInformationMessageChanged(string value)
        {
            IsMessageOn = !string.IsNullOrWhiteSpace(value);
        }

        private void ClearOperationMessages()
        {
            InformationMessage = string.Empty;
            HmesMessage = string.Empty;
            IsMessageOn = false;
        }
        [RelayCommand]
        private async Task RetryPendingScanout(PendingScanoutItem item)
        {
            if (item == null || IsPendingScanoutRetrying)
            {
                return;
            }

            ClearOperationMessages();
            await RetryPendingScanoutAsync(item);
        }

        [RelayCommand]
        private async Task RetryAllPendingScanouts()
        {
            ClearOperationMessages();
            await RetryAllPendingScanoutsAsync();
        }

        [RelayCommand]
        private async Task TrayComplete()
        {
            ClearOperationMessages();

            if (IsDataSending)
            {
                return;
            }

            if (SelectedRunMode == RunMode.None)
            {
                ShowWarningMessage("Vui lòng chọn Run Mode trước khi Tray Complete.");
                return;
            }

            try
            {
                IsDataSending = true;
                _loggingService.LogInformation($"Sending TRAY_COMPLETE to Cleber.MES: Mode={SelectedRunMode}, TrayId=<current open tray>");

                CleberMesScanResult response = await MeasureAsyncStep(
                    "MESV2 TRAY_COMPLETE",
                    "<current tray>",
                    () => _cleberMesService.SendTrayCompleteAsync(SelectedRunMode, string.Empty));
                ApplyCleberMesResult(response);

                if (response.IsCompletedOk)
                {
                    _audioNotificationService.PlayOk();
                    _loggingService.LogInformation($"Cleber.MES Tray Complete OK: Tray={response.TrayId}, ResultCode={response.ResultCode}, Message={response.Message}");
                    return;
                }

                HandleCleberMesFailure(string.Empty, response, "Cleber.MES TrayComplete");
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Pipe lỗi khi gửi Tray Complete: {ex.Message}");
                _loggingService.LogError($"Cleber.MES TrayComplete pipe error: {ex.Message}");
            }
            finally
            {
                IsDataSending = false;
            }
        }
        private string GetPendingScanoutsFilePath()
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
            string exeDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string resourcesDir = Path.Combine(exeDirectory, "Resources");
            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
            }

            return Path.Combine(resourcesDir, "pending_scanout.json");
        }
        private void LoadPendingScanouts()
        {
            try
            {
                string filePath = GetPendingScanoutsFilePath();
                if (!File.Exists(filePath))
                {
                    return;
                }

                string json = File.ReadAllText(filePath);
                var items = JsonConvert.DeserializeObject<List<PendingScanoutItem>>(json);
                if (items == null)
                {
                    return;
                }

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    PendingScanouts.Clear();
                    foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.PID)))
                    {
                        PendingScanouts.Add(item);
                    }
                });

                _loggingService.LogInformation($"[PendingScanout] Loaded {PendingScanouts.Count} pending ScanOut item(s).");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[PendingScanout] Failed to load pending ScanOut file: {ex.Message}");
            }
        }

        private void SavePendingScanouts()
        {
            try
            {
                string filePath = GetPendingScanoutsFilePath();
                List<PendingScanoutItem> items;
                lock (PendingScanouts)
                {
                    items = PendingScanouts.ToList();
                }

                string json = JsonConvert.SerializeObject(items, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[PendingScanout] Failed to save pending ScanOut file: {ex.Message}");
            }
        }

        private void SavePendingScanout(string pid, string workOrder, string partNo, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                return;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var existing = PendingScanouts.FirstOrDefault(x => x.PID.Equals(pid.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    PendingScanouts.Insert(0, new PendingScanoutItem
                    {
                        PID = pid.Trim(),
                        WorkOrder = workOrder?.Trim() ?? string.Empty,
                        PartNo = partNo?.Trim() ?? string.Empty,
                        ErrorMessage = errorMessage,
                        FailedTime = DateTime.Now
                    });
                }
                else
                {
                    existing.WorkOrder = workOrder?.Trim() ?? existing.WorkOrder;
                    existing.PartNo = partNo?.Trim() ?? existing.PartNo;
                    existing.ErrorMessage = errorMessage;
                    existing.FailedTime = DateTime.Now;
                }
            });

            SavePendingScanouts();
        }

        private void RemovePendingScanout(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                return;
            }

            bool removed = false;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var existing = PendingScanouts.FirstOrDefault(x => x.PID.Equals(pid.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    PendingScanouts.Remove(existing);
                    removed = true;
                }
            });

            if (removed)
            {
                SavePendingScanouts();
            }
        }

        private static bool ShouldPersistPendingScanout(RunMode runMode)
        {
            return runMode == RunMode.ScanOut_Rescan || runMode == RunMode.ScanOutOnly;
        }

        private static bool ShouldPersistPendingScanoutAfterResponse(RunMode runMode, CleberMesScanResult response)
        {
            if (!ShouldPersistPendingScanout(runMode))
            {
                return false;
            }

            return response.TrayLoaded;
        }

        private static string CleberMesLogContext(CleberMesScanResult response)
        {
            return $"Type={response.Type}, Success={response.Success}, Ready={response.Ready}, LotProcessed={response.LotProcessed}, TrayLoaded={response.TrayLoaded}, PartChanged={response.PartChanged}, ResultCode={response.ResultCode}, MesResultCode={response.MesResultCode}, Message={response.Message}";
        }

        private static bool IsReadyForPid(CleberMesScanResult status, string pid)
        {
            bool pidMatches = string.IsNullOrWhiteSpace(pid) ||
                string.IsNullOrWhiteSpace(status.PID) ||
                status.PID.Equals(pid.Trim(), StringComparison.OrdinalIgnoreCase);

            return status.IsReadyStatus && pidMatches;
        }

        partial void OnIsStartedChanged(bool value)
        {
            StartBtnText = value ? "STOP" : "START";
        }


        [RelayCommand]
        private async Task Start()
        {
            ClearOperationMessages();
            if (!IsStarted)
            {
                IsSessionStarting = true;
                StartBtnText = "STARTING...";

                await Task.Yield();

                try
                {
                    bool startSuccess = await Task.Run(async () =>
                    {
                        ShopFloorExePath = _configService.Config.ShopFloorExePath;
                        UpdateRunModes();

                        bool shopFloorStarted = await EnsureShopFloorRunningForSessionAsync();
                        if (!shopFloorStarted)
                        {
                            return false;
                        }

                        bool[] initResults;
                        if (RequiresScanOutUi)
                        {
                            initResults = await Task.WhenAll(
                                InitScanOutUIAndReadAsync(),
                                CheckCleberMesReadyAsync()
                            );
                        }
                        else if (IsRescanOnly)
                        {
                            _loggingService.LogInformation("RescanOnly mode: ShopFloor process started, skip ScanOut screen check.");
                            initResults = new bool[] { await CheckCleberMesReadyAsync() };
                        }
                        else
                        {
                            _loggingService.LogInformation($"Run mode {SelectedRunMode}: ShopFloor process started, skip ScanOut screen and MESV2 checks.");
                            initResults = new bool[] { true };
                        }

                        return initResults.All(result => result);
                    });

                    if (startSuccess)
                    {
                        _appState.IsRunning = true;
                        IsStarted = true;
                        _ = Task.Run(StartSerialProxyAsync);
                    }
                    else
                    {
                        _appState.IsRunning = false;
                        IsStarted = false;
                    }
                }
                catch (Exception ex)
                {
                    _appState.IsRunning = false;
                    IsStarted = false;
                    ShowWarningMessage($"START lỗi: {ex.Message}");
                    _loggingService.LogError($"Error during StartAllAsync: {ex.Message}");
                }
                finally
                {
                    IsSessionStarting = false;
                    StartBtnText = IsStarted ? "STOP" : "START";
                    BringDashboardToFront();
                }
            }
            else
            {                
                IsStarted = false;
                _appState.IsRunning = false;
                StopSerialProxy();
            }
        }
        [RelayCommand]
        private void ShopFloorClear()
        {
            ExecuteShopFloorButton("CLEAR", () =>
            {
                bool success = _shopFloorService.ClickClear();
                if (success)
                {
                    _shouldClearShopFloorBeforeNextPid = false;
                }

                return success;
            });
        }

        [RelayCommand]
        private void ShopFloorTrace()
        {
            ExecuteShopFloorButton("TRACE", () => _shopFloorService.ClickTrace());
        }

        private void ExecuteShopFloorButton(string actionName, Func<bool> action)
        {
            ClearOperationMessages();
            try
            {
                bool success = action();
                if (success)
                {
                    _loggingService.LogInformation($"ShopFloor {actionName} command executed.");
                    return;
                }

                ShowWarningMessage($"Không thể thực hiện {actionName}. Vui lòng kiểm tra màn hình ShopFloor.");
                _loggingService.LogWarning($"ShopFloor {actionName} command failed.");
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"{actionName} lỗi: {ex.Message}");
                _loggingService.LogError($"ShopFloor {actionName} error: {ex.Message}");
            }
        }

        public DashboardViewModel(
            ILoggingService loggingService,
            IConfigService configService,
            IAppState appState,
            IScanResultService scanResultService,
            IBlockRFService blockRFService,
            IAudioNotificationService audioNotificationService,
            IShopFloorService shopFloorService,
            IScanDataService scanDataService,
            ICleberMesService cleberMesService,
            IXrayInspectionService xrayInspectionService)
        {
            _loggingService = loggingService;
            _configService = configService;
            _appState = appState;
            _resultService = scanResultService;
            _blockRFService = blockRFService;
            _audioNotificationService = audioNotificationService;
            _shopFloorService = shopFloorService;
            _scanDataService = scanDataService;
            _cleberMesService = cleberMesService;
            _xrayInspectionService = xrayInspectionService;

            _loggingService.OnNewLog += LoggerService_OnNewLog;

            _loggingService.LogInformation("Initializing DashboardViewModel...");

            InitializeServices();

            //IsMessageOn=true;
            //InformationMessage = "fshkfshfkshfskfhskfhs\r\ndadsadadsd";
        }

        private void InitializeServices()
        {
            IsStarted = false;
            StartBtnText = "START";
            ShopFloorExePath = _configService.Config.ShopFloorExePath;
            UpdateRunModes();
            SelectedRunMode = LoadLastRunMode();
            LoadPendingScanouts();
            _ = Application.Current.Dispatcher.BeginInvoke(async () => await RetryAllPendingScanoutsAsync());
        }

        private void LoggerService_OnNewLog(object? sender, LogEntry e)
        {
            const int MaxLines = 100;
            string log = Logs;
            log = log + "\r\n" + $"{e.Timestamp}\t[{e.Level}]\t{e.Message}";
            var lines = log.Split('\n');
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (lines.Length > MaxLines)
                    Logs = string.Join("\n", lines.Skip(lines.Length - MaxLines));
                else
                    Logs = log;
            });
        }

        private void StartQueueConsumer()
        {
            if (_queueConsumerTask != null && !_queueConsumerTask.IsCompleted)
            {
                return;
            }

            _queueCts = new CancellationTokenSource();
            _queueConsumerTask = Task.Run(() => ProcessScanQueueAsync(_queueCts.Token));
            _loggingService.LogInformation("[Queue] Khởi tạo luồng xử lý hàng chờ quét mã (Scan Queue Consumer).");
        }

        private async Task ProcessScanQueueAsync(CancellationToken cancellationToken)
        {
            var reader = _scanQueue.Reader;
            try
            {
                while (await reader.WaitToReadAsync(cancellationToken))
                {
                    while (reader.TryRead(out QueuedScanItem queuedItem))
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        string rawData = queuedItem.Data;
                        if (string.IsNullOrWhiteSpace(rawData)) continue;

                        double queueWaitMs = Stopwatch.GetElapsedTime(queuedItem.EnqueuedTimestamp).TotalMilliseconds;
                        _loggingService.LogInformation($"[Timing] Scan queue wait: PID={rawData}, ElapsedMs={queueWaitMs:0}");
                        _loggingService.LogInformation($"[Queue] Đang lấy mã từ hàng chờ ra xử lý: {rawData}");
                        await HandleScannerDataReceivedAsync(rawData);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogInformation("[Queue] Dừng luồng hàng chờ.");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Queue] Lỗi trong luồng xử lý hàng chờ: {ex.Message}");
            }
        }

        private async Task StartSerialProxyAsync()
        {
            SerialPortSettingViewModel comDevice = _configService.Config.ScannerPortSettingVM; // SerialPortSettingViewModel comDevice
            SerialPortSettingViewModel comApp = _configService.Config.ShopFloorPortSettingVM; // SerialPortSettingViewModel comApp
            if (comDevice == null || comApp == null)
            {
                _loggingService.LogError("COM device or app settings are not configured.");
                return;
            }

            StartQueueConsumer();

            _serialProxyManager = new SerialProxyManager
            {
                Logger = new SerilogProxyLogger(_loggingService)
            };

            _serialProxyManager.OnScannerDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    string rawData = e.Data.Trim();
                    _loggingService.LogInformation($"[Queue] Nhận dữ liệu từ scanner, đưa vào hàng chờ: {rawData}");
                    _scanQueue.Writer.TryWrite(new QueuedScanItem
                    {
                        Data = rawData,
                        EnqueuedTimestamp = Stopwatch.GetTimestamp()
                    });
                }
            };

            _serialProxyManager.OnAppDataReceived += async (s, e) =>
            {
                _loggingService.LogInformation($"[Event] App gửi: {e.Data}");
                await _serialProxyManager.SendToScannerAsync(e.Data);
            };

            // Bắt đầu proxy (COM3 là thiết bị, COM5 là app chính, sniff sẽ tự tạo là COM4)
            await _serialProxyManager.StartProxy(comDevice.SelectedPort, comApp.SelectedPort, comDevice.SelectedBaudRate, comDevice.SelectedParity, comDevice.SelectedDataBits, comDevice.SelectedStopBits);
        }

        private async Task HandleScannerDataReceivedAsync(string data)
        {
            await _scanProcessingLock.WaitAsync();
            string pid = data?.Trim() ?? string.Empty;
            Stopwatch processingSw = Stopwatch.StartNew();
            try
            {
                ClearOperationMessages();

                if (await TryHandleSpecialScannerDataAsync(data))
                {
                    return;
                }

                // Kiểm tra và loại bỏ dữ liệu scan trùng lặp từ scanner gửi tới liên tiếp trong khoảng thời gian ngắn
                TimeSpan timeSinceLastScan = DateTime.Now - _lastScannedTime;
                if (!string.IsNullOrEmpty(pid) &&
                    string.Equals(pid, _lastScannedPid, StringComparison.OrdinalIgnoreCase) &&
                    timeSinceLastScan.TotalMilliseconds < DuplicateScanThresholdMs)
                {
                    _loggingService.LogWarning($"[Scanner] Trùng PID '{pid}' phát hiện trong {timeSinceLastScan.TotalMilliseconds:0}ms (Scanner scan 2 lần liên tiếp). Bỏ qua lượt quét trùng này.");
                    return;
                }

                _lastScannedPid = pid;
                _lastScannedTime = DateTime.Now;

                if (_configService.Config.BeepOnScanReceived)
                {
                    _audioNotificationService.PlayBeep();
                }
                _loggingService.LogInformation($"[Event] Device gửi: {data}");

                PID = pid;

                if (IsRescanOnly)
                {
                    _loggingService.LogInformation($"[Event] {pid} - Rescan only mode, skipping block checks and sending PID only to Cleber.MES.");
                    await SendRescanOnlyAsync(pid);
                    return;
                }

                if (IsNone)
                {
                    _loggingService.LogInformation($"[Event] {pid} - None mode, skipping block checks and forwarding to app only.");
                    await _serialProxyManager.SendToAppAsync(data);
                    return;
                }

                if (await IsBlockedByRfAsync(pid))
                {
                    return;
                }

                if (await IsBlockedByXrayAsync(pid))
                {
                    return;
                }

                _loggingService.LogInformation($"Check QA block: {data}");
                QaBlockAction qaBlockAction = await CheckQaBlockAsync(pid);
                if (qaBlockAction == QaBlockAction.StopBeforeShopFloor)
                {
                    return;
                }

                if (!ClearShopFloorBeforeNextPidIfNeeded(pid))
                {
                    return;
                }

                Stopwatch sendToShopFloorSw = Stopwatch.StartNew();
                try
                {
                    await _serialProxyManager.SendToAppAsync(data);
                }
                finally
                {
                    _loggingService.LogInformation($"[Timing] Send PID to ShopFloor: PID={pid}, ElapsedMs={sendToShopFloorSw.ElapsedMilliseconds}");
                }

                await ProcessShopFloorResultAfterForwardAsync(data);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Lỗi khi xử lý dữ liệu scanner: {ex.Message}");
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] Total scan processing: PID={pid}, ElapsedMs={processingSw.ElapsedMilliseconds}");
                _scanProcessingLock.Release();
            }
        }

        private async Task<bool> TryHandleSpecialScannerDataAsync(string data)
        {
            string scannerCommand = data.Trim();

            if (scannerCommand.Equals("CHOOSE", StringComparison.OrdinalIgnoreCase))
            {
                IsInChooseEBRMode = !IsInChooseEBRMode;
                InformationMessage = IsInChooseEBRMode ? "Vui lòng scan PCB PartNo mà bạn muốn Rescan" : "";
                IsMessageOn = IsInChooseEBRMode;
                _loggingService.LogInformation($"[Event] {data} - Is in Choose Mode: {IsInChooseEBRMode}");
                return true;
            }

            if (scannerCommand.Equals("TRACE", StringComparison.OrdinalIgnoreCase) && IsConfirmEBRStep)
            {
                SelectedEBR = _configService.Config.IsWOMode ? WorkOrder : PartNo;
                InformationMessage = "";
                IsMessageOn = false;
                IsConfirmEBRStep = false;
                IsInChooseEBRMode = false;
                await SendDataExecuteAsync();
                ExecuteShopFloorButton("TRACE", () => _shopFloorService.ClickTrace());
                _loggingService.LogInformation($"[Event] {data} - Is in Choose Mode: {IsInChooseEBRMode}");
                return true;
            }

            if (scannerCommand.Equals("TRACE", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteShopFloorButton("TRACE", () => _shopFloorService.ClickTrace());
                _loggingService.LogInformation($"[Event] {data} - TRACE command handled by ShopFloor automation.");
                return true;
            }

            if (scannerCommand.Equals("CLEAR", StringComparison.OrdinalIgnoreCase) && IsInChooseEBRMode)
            {
                InformationMessage = "";
                IsMessageOn = false;
                IsConfirmEBRStep = false;
                IsInChooseEBRMode = false;
                _loggingService.LogInformation($"[Event] {data} - Is in Choose Mode: {IsInChooseEBRMode}");
                return true;
            }

            if (scannerCommand.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                _shouldClearShopFloorBeforeNextPid = false;
            }

            return false;
        }

        private async Task<bool> IsBlockedByRfAsync(string pid)
        {
            Stopwatch sw = Stopwatch.StartNew();
            RFInfo rFInfo = await _blockRFService.IsBlock(pid);
            _loggingService.LogInformation($"[Timing] RF block check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            if (rFInfo == null)
            {
                return false;
            }

            ShowWarningMessage($"[BLOCK RF] PID {pid} đã bị chặn do NG RF ở jig {rFInfo.MachineIP} band {rFInfo.Band} signal {rFInfo.Signpath} antenna number {rFInfo.AntNumber} value {rFInfo.ValueNg}");
            _loggingService.LogWarning($"PID {pid} bị RF block");
            return true;
        }

        private async Task<bool> IsBlockedByXrayAsync(string pid)
        {
            Stopwatch sw = Stopwatch.StartNew();
            XrayTraceResult xrayResult = await _xrayInspectionService.GetBlockingResultAsync(pid);
            _loggingService.LogInformation($"[Timing] Xray block check: PID={pid}, ElapsedMs={sw.ElapsedMilliseconds}");
            if (xrayResult == null)
            {
                return false;
            }

            ShowWarningMessage($"[BLOCK XRAY] PID {pid} đã bị chặn do Xray NG.");
            _loggingService.LogWarning($"PID {pid} bi Xray block. Machine={xrayResult.MachineResult}, AI={xrayResult.AiResult}, User={xrayResult.UserResult}, Final={xrayResult.FinalResult}, Confirmed={xrayResult.UserConfirmed}, ClientTime={xrayResult.ClientTime}");
            return true;
        }

        private async Task<QaBlockAction> CheckQaBlockAsync(string pid)
        {
            var (isQaBlocked, reason) = await _scanDataService.IsBlockedAsync(pid);

            if (!isQaBlocked)
            {
                return QaBlockAction.AllowFullFlow;
            }

            ShowQaBlockedMessage(pid, reason);
            _loggingService.LogWarning($"PID {pid} bị QA block do {reason}");
            return QaBlockAction.StopBeforeShopFloor;
        }

        private void ShowQaBlockedMessage(string pid, string reason)
        {
            ShowWarningMessage($"[BLOCK QA] PID {pid} đã bị block do {reason}, vui lòng kiểm tra lại");
        }

        private bool ClearShopFloorBeforeNextPidIfNeeded(string pid)
        {
            if (!_shouldClearShopFloorBeforeNextPid)
            {
                return true;
            }

            try
            {
                _loggingService.LogInformation($"Previous ShopFloor result was NG. Executing CLEAR before PID {pid}.");
                bool success = _shopFloorService.ClickClear();
                if (success)
                {
                    _shouldClearShopFloorBeforeNextPid = false;
                    return true;
                }

                ShowWarningMessage($"Không thể CLEAR ShopFloor trước khi gửi PID {pid}. Vui lòng kiểm tra ShopFloor.");
                _loggingService.LogWarning($"Failed to execute CLEAR before PID {pid}.");
                return false;
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"CLEAR trước PID {pid} lỗi: {ex.Message}");
                _loggingService.LogError($"CLEAR before PID {pid} error: {ex.Message}");
                return false;
            }
        }

        private async Task ProcessShopFloorResultAfterForwardAsync(string sentData)
        {
            if (sentData.Contains("CLEAR") || sentData.Contains("TRACE") || IsNone)
            {
                return;
            }

            if (!_shopFloorService.IsScanoutUI()) return;

            ShopFloorScanResult scanResult = await ReadScanOutResult(sentData);
            bool isWorkAlreadyFinished = IsWorkAlreadyFinishedResult(scanResult);
            if (!scanResult.IsOk && !isWorkAlreadyFinished)
            {
                _shouldClearShopFloorBeforeNextPid = true;
                _loggingService.LogWarning($"ShopFloor NG for PID {sentData.Trim()}. CLEAR will be executed before next PID.");
                return;
            }

            if (isWorkAlreadyFinished)
            {
                _shouldClearShopFloorBeforeNextPid = true;
                _loggingService.LogWarning(
                    $"ShopFloor NG accepted for MESV2: PID={scanResult.PID}, " +
                    $"ShopFloorResult={scanResult.Result}, ShopFloorMessage='{scanResult.Message}', " +
                    "MesV2Result=OK, MesV2Message=''. CLEAR will be executed before next PID.");
            }

            if (!IsInChooseEBRMode)
            {
                await SendDataExecuteAsync();
            }
            else
            {
                InformationMessage = _configService.Config.IsWOMode
                    ? $"Bạn có muốn chọn {WorkOrder} để Rescan không?\r\n Nếu có scan TRACE"
                    : $"Bạn có muốn chọn {PartNo} để Rescan không?\r\n Nếu có scan TRACE";

                IsMessageOn = true;
                IsConfirmEBRStep = true;
            }
        }

        private async Task SendRescanOnlyAsync(string data)
        {
            await SendCleberMesRescanOnlyAsync(data);
        }

        private void ShowWarningMessage(string message)
        {
            if (ShouldPrefixMesV2Warning(message))
            {
                message = $"[LỖI MESV2] {message}";
            }

            _audioNotificationService.PlayNg();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                InformationMessage = message;
                IsMessageOn = true;
            });
        }

        private void ShowMesV2WarningMessage(string message)
        {
            ShowWarningMessage($"[LỖI MESV2] {message}");
        }

        private static bool ShouldPrefixMesV2Warning(string message)
        {
            if (string.IsNullOrWhiteSpace(message) ||
                message.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] mesV2Signals =
            {
                "Cleber.MES",
                "MESV2",
                "Named Pipe",
                "Retry ScanOutOnly",
                "RescanOnly PID",
                "Khác PartNo",
                "Scan out",
                "packed"
            };

            if (message.StartsWith("Pipe ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return mesV2Signals.Any(signal =>
                message.Contains(signal, StringComparison.OrdinalIgnoreCase));
        }

        private void StopSerialProxy()
        {
            // Dừng proxy
            _serialProxyManager?.Stop();
        }

        private async Task<bool> EnsureShopFloorRunningForSessionAsync()
        {
            try
            {
                await _shopFloorService.EnsureRunningAsync();
                return true;
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Không thể bật ShopFloor: {ex.Message}");
                _loggingService.LogError($"Failed to start ShopFloor process: {ex.Message}");
                return false;
            }
        }

        private async Task ValidateScanOutModeAfterRunModeChangedAsync(RunMode runMode)
        {
            int validationVersion = Interlocked.Increment(ref _runModeValidationVersion);
            _loggingService.LogInformation($"Run mode changed to {runMode} while running. Checking ShopFloor ScanOut screen.");

            bool isReady = await Task.Run(async () =>
            {
                bool shopFloorReady = await InitScanOutUIAndReadAsync();
                if (!shopFloorReady)
                {
                    return false;
                }

                return await CheckCleberMesReadyAsync();
            });

            if (validationVersion != _runModeValidationVersion ||
                SelectedRunMode != runMode ||
                !IsStarted)
            {
                _loggingService.LogInformation($"Ignored stale run mode validation result for {runMode}.");
                return;
            }

            if (isReady)
            {
                _loggingService.LogInformation($"Run mode {runMode} is ready for ScanOut.");
            }
        }

        private void StopScanSessionForCleberNotReady(CleberMesScanResult response)
        {
            IsStarted = false;
            _appState.IsRunning = false;
            StopSerialProxy();
            _loggingService.LogWarning($"Cleber.MES not ready. Scanning stopped. {CleberMesLogContext(response)}");
        }

        private void HandleCleberMesFailure(string pid, CleberMesScanResult response, string logPrefix)
        {
            if (response.Completed)
            {
                _loggingService.LogInformation($"{logPrefix} tray completed: PID={pid}, Tray={response.TrayId}, Qty={response.CurrentQty}/{response.MaxQty}, {CleberMesLogContext(response)}");
                return;
            }

            if (response.IsStatusResult && !response.Ready)
            {
                StopScanSessionForCleberNotReady(response);
                ShowWarningMessage($"Cleber.MES not ready: {response.ResultCode}: {response.Message}");
                return;
            }

            if (response.IsPartChanged)
            {
                ShowWarningMessage($"{response.PID} Khác PartNo: {response.Message}");
                _loggingService.LogWarning($"{logPrefix} PART_NO_CHANGED: PID={pid}, Tray={response.TrayId}, Qty={response.CurrentQty}/{response.MaxQty}, {CleberMesLogContext(response)}");
                return;
            }

            if (response.ResultCode.Equals("LOT_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
                response.Message.Contains("The Lot ID does not exist."))
            {
                ShowWarningMessage($"{response.PID} Chưa được Scan out hoặc chưa được Scan out bằng MESV2: {response.Message}");
                _loggingService.LogWarning($"{logPrefix} NG: PID={pid}, {CleberMesLogContext(response)}");
                return;
            }

            if (response.ResultCode.Equals("ALREADY_PACKED", StringComparison.OrdinalIgnoreCase) ||
                response.Message.Contains("The Lot is already packed in another tray."))
            {
                string packedTrayId = "";
                string[] messageSplit = response.Message.Split(":");
                if (messageSplit.Length > 1)
                {
                    packedTrayId = messageSplit[1].Trim();
                }
                ShowWarningMessage($"{response.PID} đã được đóng gói vào tray {packedTrayId}: {response.Message}");
                _loggingService.LogWarning($"{logPrefix} NG: PID={pid}, {CleberMesLogContext(response)}");
                return;
            }


            ShowWarningMessage($"Cleber.MES NG: {response.ResultCode}: {response.Message}");
            _loggingService.LogWarning($"{logPrefix} NG: PID={pid}, {CleberMesLogContext(response)}");
        }

        private async Task<bool> InitScanOutUIAndReadAsync()
        {
            const string scanOutScreenMessage = "ShopFloor không ở chế độ PCB Output, hãy chuyển sang chế độ PCB Output";

            try
            {
                await _shopFloorService.InitializeAsync();
                if (!_shopFloorService.IsScanoutUI())
                {
                    ShowWarningMessage(scanOutScreenMessage);
                    _loggingService.LogWarning("ShopFloor UI is not on the ScanOut screen after initialization.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"{scanOutScreenMessage}. {ex.Message}");
                _loggingService.LogError($"Failed to initialize AutoScanOutUI: {ex.Message}");
                return false;
            }

            try
            {
                _loggingService.LogInformation("Reading initial ShopFloor UI data...");
                var scanResult = await RunWithTimeoutAsync(
                    () => _shopFloorService.ReadCurrent(),
                    TimeSpan.FromSeconds(10),
                    "Read initial ShopFloor UI data");
                ApplyShopFloorResult(scanResult);
                _loggingService.LogInformation("Initial ShopFloor UI data read successfully.");
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Không thể đọc dữ liệu ShopFloor ban đầu: {ex.Message}");
                _loggingService.LogError($"Failed to read initial ShopFloor UI data: {ex.Message}");
                return false;
            }

            return true;
        }

        private async Task<bool> CheckCleberMesReadyAsync(string pid = "")
        {
            try
            {
                CleberMesScanResult status = await MeasureAsyncStep(
                    "MESV2 STATUS",
                    string.IsNullOrWhiteSpace(pid) ? "<session>" : pid,
                    () => string.IsNullOrWhiteSpace(pid)
                        ? _cleberMesService.GetStatusAsync()
                        : _cleberMesService.GetStatusAsync(pid));
                ApplyCleberMesResult(status);

                bool pidMatches = string.IsNullOrWhiteSpace(pid) ||
                    string.IsNullOrWhiteSpace(status.PID) ||
                    status.PID.Equals(pid.Trim(), StringComparison.OrdinalIgnoreCase);

                if (IsReadyForPid(status, pid))
                {
                    _loggingService.LogInformation($"Cleber.MES ready: PID={pid}, Plant={status.PlantCode}, Line={status.LineCode}, Operation={status.OperationCode}, Lane={status.LaneCode}, {CleberMesLogContext(status)}");
                    return true;
                }

                ShowWarningMessage($"Cleber.MES is not ready: {status.ResultCode} {status.Message}");
                _loggingService.LogWarning($"Cleber.MES status failed: RequestedPID={pid}, ResponsePID={status.PID}, PIDMatched={pidMatches}, {CleberMesLogContext(status)}");
                return false;
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Cannot connect to Cleber.MES Named Pipe: {ex.Message}");
                _loggingService.LogError($"Cleber.MES status error: {ex.Message}");
                return false;
            }
        }

        private static void BringDashboardToFront()
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Window mainWindow = Application.Current.MainWindow;
                if (mainWindow == null)
                {
                    return;
                }

                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }

                mainWindow.Topmost = true;
                mainWindow.Activate();
                mainWindow.Focus();
                mainWindow.Topmost = false;
            }, DispatcherPriority.ApplicationIdle);
        }

        private async Task RetryAllPendingScanoutsAsync()
        {
            if (IsPendingScanoutRetrying || PendingScanouts.Count == 0)
            {
                return;
            }

            List<PendingScanoutItem> items = PendingScanouts.ToList();
            foreach (var item in items)
            {
                await RetryPendingScanoutAsync(item);
            }
        }

        private async Task<bool> RetryPendingScanoutAsync(PendingScanoutItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PID))
            {
                return false;
            }

            try
            {
                ClearOperationMessages();
                IsPendingScanoutRetrying = true;
                item.RetryCount++;
                item.LastRetryTime = DateTime.Now;
                _loggingService.LogInformation($"[PendingScanout] Retrying PID '{item.PID}' with ScanOutOnly mode.");

                CleberMesScanResult status = await MeasureAsyncStep(
                    "MESV2 pending STATUS",
                    item.PID,
                    () => _cleberMesService.GetStatusAsync(item.PID));
                ApplyCleberMesResult(status);
                if (!IsReadyForPid(status, item.PID))
                {
                    item.ErrorMessage = $"Cleber.MES not ready: {status.ResultCode} {status.Message}".Trim();
                    SavePendingScanouts();
                    HandleCleberMesFailure(item.PID, status, "Cleber.MES PendingScanout STATUS");
                    _loggingService.LogWarning($"[PendingScanout] Retry status failed for PID '{item.PID}': {CleberMesLogContext(status)}");
                    return false;
                }

                CleberMesScanResult response = await MeasureAsyncStep(
                    "MESV2 pending SCAN",
                    item.PID,
                    () => _cleberMesService.SendScanAsync(
                        RunMode.ScanOutOnly,
                        item.PID,
                        item.WorkOrder,
                        item.PartNo,
                        string.Empty,
                        string.Empty));

                ApplyCleberMesResult(response);

                if (response.IsSuccessfulScan)
                {
                    _audioNotificationService.PlayOk();
                    RemovePendingScanout(item.PID);
                    _loggingService.LogInformation($"[PendingScanout] Retry OK. Removed PID '{item.PID}' from pending list. {CleberMesLogContext(response)}");
                    await PushQaOutputAfterMesSuccessAsync(item.PID, item.WorkOrder, item.PartNo, response);
                    return true;
                }

                item.ErrorMessage = $"Cleber.MES NG: {response.ResultCode} {response.Message}".Trim();
                HandleCleberMesFailure(item.PID, response, "Cleber.MES PendingScanout");
                if (ShouldPersistPendingScanoutAfterResponse(RunMode.ScanOutOnly, response))
                {
                    SavePendingScanouts();
                    _loggingService.LogWarning($"[PendingScanout] Retry failed for PID '{item.PID}', keeping pending: {item.ErrorMessage}. {CleberMesLogContext(response)}");
                }
                else
                {
                    RemovePendingScanout(item.PID);
                    _loggingService.LogWarning($"[PendingScanout] Retry failed for PID '{item.PID}', removed from pending because retry is not required: {item.ErrorMessage}. {CleberMesLogContext(response)}");
                }

                return false;
            }
            catch (Exception ex)
            {
                item.ErrorMessage = ex.Message;
                SavePendingScanouts();
                ShowWarningMessage($"Retry ScanOutOnly PID {item.PID} lỗi pipe: {ex.Message}");
                _loggingService.LogError($"[PendingScanout] Retry pipe error for PID '{item.PID}': {ex.Message}");
                return false;
            }
            finally
            {
                IsPendingScanoutRetrying = false;
            }
        }

        private async Task<bool> SendDataExecuteAsync()
        {
            if (IsInChooseEBRMode)
            {
                return false;
            }

            try
            {
                IsDataSending = true;
                CleberMesScanResult cleberResult = await SendCleberMesScanAsync(PID);
                return cleberResult.IsSuccessfulScan;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                _loggingService.LogError($"Error sending data to Cleber.MES: {ex.Message}");
                return false;
            }
            finally
            {
                IsDataSending = false;
            }
        }

        public async Task<ShopFloorScanResult> ReadScanOutResult(string pid)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var scanResult = await _shopFloorService.WaitForResultAsync(pid);
                ApplyShopFloorResult(scanResult);

                _loggingService.LogInformation($"Data from ScanOut GUI:{PID},{PartNo},{WorkOrder},{Result}:{sw.ElapsedMilliseconds}ms");

                if (!scanResult.IsOk)
                {
                    _loggingService.LogInformation($"ScanOut NG:{pid},{PartNo},{WorkOrder},{Result},{ResultMessage}:{sw.ElapsedMilliseconds}ms");
                }

                return scanResult;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to read scan out result: {ex.Message}");
                return new ShopFloorScanResult
                {
                    PID = pid?.Trim() ?? string.Empty,
                    Result = "NG",
                    Message = ex.Message
                };
            }

        }

        public async Task<bool> ReadScanOutResultByLog(string pid)
        {
            if(_resultService == null)
            {
                _loggingService.LogError("Result service is not initialized.");
                return false;
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var result = await _resultService.RequestResultAsync(pid.Trim(),3000);

            //PID = result.PID;
            //WorkOrder = result.WorkOrder;
            //PartNo = result.Model;
            //Result = result.Result;
            _loggingService.LogInformation($"Data from ScanOut LOG:{result.PID},{result.Model.TrimEnd('.')},{result.WorkOrder},{result.Result}:{sw.ElapsedMilliseconds}ms");
            sw.Stop();
            sw.Reset();
            return result.Result == "OK" ? true : false;
        }


        private void ReadScanOutGUI()
        {
            ApplyShopFloorResult(_shopFloorService.ReadCurrent());
        }

        private void ApplyShopFloorResult(ShopFloorScanResult scanResult)
        {
            PID = scanResult.PID;
            WorkOrder = scanResult.WorkOrder;
            PartNo = scanResult.PartNo;
            Result = scanResult.Result;
            Progress = scanResult.Progress;
            ResultMessage = scanResult.Message;
        }

        private static bool IsWorkAlreadyFinishedResult(ShopFloorScanResult scanResult)
        {
            return scanResult != null &&
                   string.Equals(scanResult.Result, "NG", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(scanResult.Message?.Trim(), WorkAlreadyFinishedMessage, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<CleberMesScanResult> SendCleberMesScanAsync(string pid)
        {
            ClearOperationMessages();
            bool isWorkAlreadyFinished =
                string.Equals(Result, "NG", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResultMessage?.Trim(), WorkAlreadyFinishedMessage, StringComparison.OrdinalIgnoreCase);
            string mesV2Result = isWorkAlreadyFinished ? "OK" : Result;
            string mesV2Message = isWorkAlreadyFinished ? string.Empty : ResultMessage;

            _loggingService.LogInformation(
                $"Sending PID '{pid}' to Cleber.MES: Mode={SelectedRunMode}, WorkOrder={WorkOrder}, PartNo={PartNo}, " +
                $"ShopFloorResult={Result}, ShopFloorMessage='{ResultMessage}', " +
                $"MesV2Result={mesV2Result}, MesV2Message='{mesV2Message}'");

            CleberMesScanResult response;
            try
            {
                CleberMesScanResult status = await MeasureAsyncStep(
                    "MESV2 STATUS",
                    pid,
                    () => _cleberMesService.GetStatusAsync(pid));
                ApplyCleberMesResult(status);
                if (!IsReadyForPid(status, pid))
                {
                    if (ShouldPersistPendingScanout(SelectedRunMode))
                    {
                        string errorMessage = $"Cleber.MES not ready for PID {pid}: {status.ResultCode} {status.Message}".Trim();
                        SavePendingScanout(pid, WorkOrder, PartNo, errorMessage);
                        _loggingService.LogWarning($"Cleber.MES STATUS not ready. Pending saved for PID={pid}, Mode={SelectedRunMode}, WorkOrder={WorkOrder}, PartNo={PartNo}. {CleberMesLogContext(status)}");
                    }
                    else
                    {
                        _loggingService.LogWarning($"Cleber.MES STATUS not ready. Pending not saved for PID={pid}, Mode={SelectedRunMode}. {CleberMesLogContext(status)}");
                    }

                    HandleCleberMesFailure(pid, status, "Cleber.MES STATUS");
                    return status;
                }

                response = await MeasureAsyncStep(
                    "MESV2 SCAN",
                    pid,
                    () => _cleberMesService.SendScanAsync(
                        SelectedRunMode,
                        pid,
                        WorkOrder,
                        PartNo,
                        IsScanOutRescan ? mesV2Result : string.Empty,
                        IsScanOutRescan ? mesV2Message : string.Empty));
            }
            catch (Exception ex)
            {
                if (ShouldPersistPendingScanout(SelectedRunMode))
                {
                    SavePendingScanout(pid, WorkOrder, PartNo, ex.Message);
                    ShowWarningMessage($"Pipe lỗi khi gửi PID {pid}. Đã lưu vào danh sách retry.");
                    _loggingService.LogError($"Cleber.MES pipe error. Pending saved for PID={pid}, Mode={SelectedRunMode}, WorkOrder={WorkOrder}, PartNo={PartNo}: {ex.Message}");
                }
                else
                {
                    ShowWarningMessage($"Pipe lỗi khi gửi PID {pid}: {ex.Message}");
                    _loggingService.LogError($"Cleber.MES pipe error. Pending not saved for PID={pid}, Mode={SelectedRunMode}: {ex.Message}");
                }

                throw;
            }

            ApplyCleberMesResult(response);

            if (response.IsSuccessfulScan)
            {
                _audioNotificationService.PlayOk();
                if (ShouldPersistPendingScanout(SelectedRunMode))
                {
                    RemovePendingScanout(pid);
                }

                _loggingService.LogInformation($"Cleber.MES OK: PID={pid}, Tray={response.TrayId}, Qty={response.CurrentQty}/{response.MaxQty}, Completed={response.Completed}, {CleberMesLogContext(response)}");
                await PushQaOutputAfterMesSuccessAsync(pid, WorkOrder, PartNo, response);
            }
            else
            {
                if (ShouldPersistPendingScanoutAfterResponse(SelectedRunMode, response))
                {
                    string errorMessage = $"Cleber.MES NG: {response.ResultCode} {response.Message}".Trim();
                    SavePendingScanout(pid, WorkOrder, PartNo, errorMessage);
                    _loggingService.LogWarning($"Cleber.MES NG after ShopFloor OK. Pending saved for PID={pid}, Mode={SelectedRunMode}, WorkOrder={WorkOrder}, PartNo={PartNo}. {CleberMesLogContext(response)}");
                }
                else if (ShouldPersistPendingScanout(SelectedRunMode))
                {
                    RemovePendingScanout(pid);
                    _loggingService.LogInformation($"Cleber.MES NG for PID={pid}, retry not required. Pending removed/skipped. Mode={SelectedRunMode}, {CleberMesLogContext(response)}");
                }

                HandleCleberMesFailure(pid, response, "Cleber.MES");
            }

            return response;
        }

        private async Task<CleberMesScanResult> SendCleberMesRescanOnlyAsync(string pid)
        {
            ClearOperationMessages();
            _loggingService.LogInformation($"Sending PID '{pid}' to Cleber.MES with RescanOnly request.");

            CleberMesScanResult response;
            try
            {
                CleberMesScanResult status = await MeasureAsyncStep(
                    "MESV2 RescanOnly STATUS",
                    pid,
                    () => _cleberMesService.GetStatusAsync(pid));
                ApplyCleberMesResult(status);
                if (!IsReadyForPid(status, pid))
                {
                    _loggingService.LogWarning($"Cleber.MES RescanOnly STATUS not ready. Pending not saved for PID={pid}. {CleberMesLogContext(status)}");
                    HandleCleberMesFailure(pid, status, "Cleber.MES RescanOnly STATUS");
                    return status;
                }

                response = await MeasureAsyncStep(
                    "MESV2 RESCAN_ONLY",
                    pid,
                    () => _cleberMesService.SendRescanOnlyAsync(pid));
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Pipe lỗi khi gửi RescanOnly PID {pid}: {ex.Message}");
                _loggingService.LogError($"Cleber.MES RescanOnly pipe error. Pending not saved for PID={pid}: {ex.Message}");
                throw;
            }

            ApplyCleberMesResult(response);

            if (response.IsSuccessfulScan)
            {
                _audioNotificationService.PlayOk();
                _loggingService.LogInformation($"Cleber.MES RescanOnly OK: PID={pid}, Tray={response.TrayId}, Qty={response.CurrentQty}/{response.MaxQty}, Completed={response.Completed}, {CleberMesLogContext(response)}");
                await PushQaOutputAfterMesSuccessAsync(pid, null, null, response);
            }
            else
            {
                HandleCleberMesFailure(pid, response, "Cleber.MES RescanOnly");
            }

            return response;
        }

        private async Task PushQaOutputAfterMesSuccessAsync(
            string pid,
            string workOrder,
            string partNo,
            CleberMesScanResult mesResponse)
        {
            var item = new AvPidInoutDto
            {
                Pid = pid?.Trim() ?? string.Empty,
                MachineId = null,
                MachineName = string.IsNullOrWhiteSpace(_configService.Config.QaMachineName)
                    ? null
                    : _configService.Config.QaMachineName.Trim(),
                OutputTime = DateTime.Now,
                SlotNo = mesResponse?.CurrentQty > 0 ? mesResponse.CurrentQty : null,
                PackNo = string.IsNullOrWhiteSpace(mesResponse?.TrayId) ? null : mesResponse.TrayId.Trim(),
                Wo = string.IsNullOrWhiteSpace(workOrder) ? null : workOrder.Trim(),
                Ebr = string.IsNullOrWhiteSpace(partNo) ? null : partNo.Trim()
            };

            try
            {
                PushResultDto result = await _scanDataService.PushAvPidOutputAsync(item);
                if (!result.Success)
                {
                    _loggingService.LogWarning($"[QA Output] Server returned unsuccessful result after MESV2 OK. PID={item.Pid}, Message={result.Message}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[QA Output] Push failed after MESV2 OK. PID={item.Pid}, Error={ex.Message}");
            }
        }

        private void ApplyCleberMesResult(CleberMesScanResult response)
        {
            HmesResult = response.ResultCode;
            HmesMessage = response.Message;

            if (response.Completed)
            {
                ClearCleberMesTrayDisplay();
                return;
            }

            HmesPackQty = response.CurrentQty;
            HmesMagazineQty = response.MaxQty;
            HmesMagazineNo = response.TrayId;
        }

        private void ClearCleberMesTrayDisplay()
        {
            HmesPackQty = 0;
            HmesMagazineQty = 0;
            HmesMagazineNo = string.Empty;
        }

        private static void ShowErrorDialog(string message)
        {
            MessageBox.Show(Application.Current.MainWindow, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async Task<T> MeasureAsyncStep<T>(string step, string reference, Func<Task<T>> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                return await action();
            }
            finally
            {
                _loggingService.LogInformation($"[Timing] {step}: Reference={reference}, ElapsedMs={sw.ElapsedMilliseconds}");
            }
        }

        public class SerilogProxyLogger : IProxyLogger
        {
            private readonly ILoggingService _logger;
            public SerilogProxyLogger(ILoggingService logger)
            {
                _logger = logger;
            }

            public void Log(string message)
            {
                _logger.LogInformation(message);
            }
        }

    }
}




