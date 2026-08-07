using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataExcuter;
using ScanOutLogLib.Services;
using ScanOutTool.Models;
using ScanOutTool.Services;
using ScanOutTool.Utilities;
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
        private enum HmesBlockAction
        {
            AllowFullFlow,
            SkipWebAfterShopFloor,
            StopBeforeShopFloor
        }

        private readonly IAppState _appState;
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;
        private readonly IScanResultService _resultService;
        private readonly IBlockRFService _blockRFService;
        private readonly IAudioNotificationService _audioNotificationService;
        private readonly IShopFloorService _shopFloorService;
        private readonly IScanDataService _scanDataService;

        private SerialProxyManager _serialProxyManager;
        private readonly HashSet<string> _skipWebSendPids = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _hmesRefreshTimer = new();
        private bool _shouldClearShopFloorBeforeNextPid;
        private bool _isRefreshingHmesInfo;
        private readonly System.Threading.SemaphoreSlim _scanProcessingLock = new(1, 1);
        private string _lastScannedPid = string.Empty;
        private DateTime _lastScannedTime = DateTime.MinValue;
        private const int DuplicateScanThresholdMs = 3000;

        private readonly Channel<string> _scanQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private CancellationTokenSource? _queueCts;
        private Task? _queueConsumerTask;

        private bool IsScanOutOnly => SelectedRunMode == RunMode.ScanOutOnly;
        private bool IsRescanOnly => SelectedRunMode == RunMode.RescanOnly;
        private bool IsScanOutRescan => SelectedRunMode == RunMode.ScanOut_Rescan;
        private bool IsNone => SelectedRunMode == RunMode.None;

        private Stopwatch mainSW = new Stopwatch();

        [ObservableProperty] private bool isStarted;
        [ObservableProperty] private bool isSessionStarting;
        [ObservableProperty] private bool isStartButtonVisible = true;
        [ObservableProperty] private bool isInChooseEBRMode=false;
        [ObservableProperty] private bool isPCBtoSelectEBR = false;
        [ObservableProperty] private bool isConfirmEBRStep = false;
        [ObservableProperty] private bool isDataSending;
        [ObservableProperty] private bool isSkipHmesWeb;

        partial void OnIsSkipHmesWebChanged(bool value)
        {
            UpdateRunModes();
        }

        private void UpdateRunModes()
        {
            if (IsSkipHmesWeb)
            {
                RunModes = new List<RunMode> { RunMode.ScanOutOnly };
                SelectedRunMode = RunMode.ScanOutOnly;
            }
            else
            {
                RunModes = Enum.GetValues(typeof(RunMode)).Cast<RunMode>().ToList();
                if (SelectedRunMode == RunMode.None || !RunModes.Contains(SelectedRunMode))
                {
                    SelectedRunMode = RunMode.ScanOut_Rescan;
                }
            }
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
        [ObservableProperty] private string hmesTransactionType = "NORMAL";
        [ObservableProperty] private bool isTransactionTypeNormal = true;
        [ObservableProperty] private bool isTransactionTypeCancel = false;
        [ObservableProperty] private bool isManualMagazineInputFocused;
        [ObservableProperty] private string inputMagazineNo = string.Empty;
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<SavedMagazineItem> savedMagazines = new();
        [ObservableProperty] private SavedMagazineItem selectedSavedMagazine;

        partial void OnSelectedSavedMagazineChanged(SavedMagazineItem value)
        {
            if (value != null && !string.IsNullOrEmpty(value.MagazineNo))
            {
                InputMagazineNo = value.MagazineNo;
            }
        }

        partial void OnInformationMessageChanged(string value)
        {
            IsMessageOn = !string.IsNullOrWhiteSpace(value);
        }

        [RelayCommand]
        private void ClearMagazine(SavedMagazineItem item)
        {
            if (item != null && SavedMagazines.Contains(item))
            {
                SavedMagazines.Remove(item);
                _loggingService.LogInformation($"Xóa Magazine No '{item.MagazineNo}' khỏi danh sách đã lưu.");
                SaveSavedMagazines();
            }
        }

        [RelayCommand]
        private void ClearAllMagazines()
        {
            if (SavedMagazines.Count > 0)
            {
                SavedMagazines.Clear();
                _loggingService.LogInformation("Đã xóa tất cả Magazine No đã lưu.");
                SaveSavedMagazines();
            }
        }

        private void AddCurrentMagazineToSavedList()
        {
            if (string.IsNullOrWhiteSpace(HmesMagazineNo)) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var existing = SavedMagazines.FirstOrDefault(x => x.MagazineNo.Equals(HmesMagazineNo, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Qty = HmesPackQty;
                    existing.Qpm = HmesMagazineQty;
                    existing.SavedTime = DateTime.Now;
                }
                else
                {
                    SavedMagazines.Insert(0, new SavedMagazineItem
                    {
                        MagazineNo = HmesMagazineNo,
                        Qty = HmesPackQty,
                        Qpm = HmesMagazineQty,
                        SavedTime = DateTime.Now
                    });
                }
                SaveSavedMagazines();
            });
        }

        private string GetSavedMagazinesFilePath()
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
            string exeDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string resourcesDir = Path.Combine(exeDirectory, "Resources");
            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
            }
            return Path.Combine(resourcesDir, "saved_magazines.json");
        }

        private void LoadSavedMagazines()
        {
            try
            {
                string filePath = GetSavedMagazinesFilePath();
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var items = JsonConvert.DeserializeObject<List<SavedMagazineItem>>(json);
                    if (items != null)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            SavedMagazines.Clear();
                            foreach (var item in items)
                            {
                                SavedMagazines.Add(item);
                            }
                        });
                        _loggingService.LogInformation($"[Persistence] Đã tải {items.Count} magazine đã lưu từ file.");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Persistence] Lỗi khi tải magazine từ file: {ex.Message}");
            }
        }

        private void SaveSavedMagazines()
        {
            try
            {
                string filePath = GetSavedMagazinesFilePath();
                List<SavedMagazineItem> items;
                lock (SavedMagazines)
                {
                    items = SavedMagazines.ToList();
                }
                string json = JsonConvert.SerializeObject(items, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[Persistence] Lỗi khi lưu magazine ra file: {ex.Message}");
            }
        }

        partial void OnIsTransactionTypeNormalChanged(bool value)
        {
            if (value)
            {
                IsTransactionTypeCancel = false;
                HmesTransactionType = "NORMAL";
                _ = Task.Run(() => _scanDataService.SetTransactionTypeAsync("NORMAL"));
            }
        }

        partial void OnIsTransactionTypeCancelChanged(bool value)
        {
            if (value)
            {
                IsTransactionTypeNormal = false;
                HmesTransactionType = "CANCEL";
                _ = Task.Run(() => _scanDataService.SetTransactionTypeAsync("CANCEL"));
            }
        }

        partial void OnIsStartedChanged(bool value)
        {
            StartBtnText = value ? "STOP" : "START";
            if (value && !IsSkipHmesWeb)
            {
                StartHmesRefreshTimer();
            }
            else
            {
                StopHmesRefreshTimer();
            }
        }


        [RelayCommand]
        private async Task Start()
        {
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
                        IsSkipHmesWeb = _configService.Config.SkipHmesWeb;
                        UpdateRunModes();

                        if (!IsSkipHmesWeb)
                        {
                            KillProcess.KillChromeAndDriver();
                        }

                        bool[] initResults;
                        if (IsSkipHmesWeb)
                        {
                            _loggingService.LogInformation("Chế độ Skip HMES Web được bật: Bỏ qua khởi tạo trình duyệt Chrome.");
                            initResults = new bool[] { await InitScanOutUIAndReadAsync(), true };
                        }
                        else
                        {
                            initResults = await Task.WhenAll(
                                InitScanOutUIAndReadAsync(),
                                StartDataExecuteSessionAsync()
                            );
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

        partial void OnHmesMagazineNoChanged(string value)
        {
        }

        [RelayCommand]
        private async Task ManualPrint()
        {
            try
            {
                IsDataSending = true;
                bool success = await _scanDataService.PrintManual();

                if (success)
                {
                    _loggingService.LogInformation("Manual print command sent successfully.");
                }
                else
                {
                    ShowWarningMessage("Không thể Manual Print. Vui lòng kiểm tra phiên HMES web.");
                    _loggingService.LogWarning("Manual print command failed.");
                }
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Manual Print lỗi: {ex.Message}");
                _loggingService.LogError($"Manual print error: {ex.Message}");
            }
            finally
            {
                IsDataSending = false;
            }
        }

        [RelayCommand]
        private async Task SaveLabel()
        {
            try
            {
                IsDataSending = true;
                // Thêm ngay thông tin Magazine hiện tại vào danh sách không cần đợi web
                AddCurrentMagazineToSavedList();

                bool success = await _scanDataService.SaveLabelAsync();

                if (success)
                {
                    await RefreshHmesInfoAsync();
                    _loggingService.LogInformation("Lưu tem thành công và đã làm mới trang HMES web.");
                }
                else
                {
                    ShowWarningMessage("Không thể Lưu tem. Vui lòng kiểm tra trang HMES web.");
                    _loggingService.LogWarning("Save label command failed.");
                }
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Lưu tem lỗi: {ex.Message}");
                _loggingService.LogError($"Save label error: {ex.Message}");
            }
            finally
            {
                IsDataSending = false;
            }
        }

        [RelayCommand]
        private async Task SendMagazineNo()
        {
            if (string.IsNullOrWhiteSpace(InputMagazineNo))
            {
                ShowWarningMessage("Vui lòng nhập Magazine No trước khi nhấn Send.");
                return;
            }

            try
            {
                IsDataSending = true;
                string targetMagNo = InputMagazineNo.Trim();
                InputMagazineNo = string.Empty;
                _loggingService.LogInformation($"Gửi Magazine No '{targetMagNo}' sang HMES Web...");
                bool success = await _scanDataService.SearchMagazineNoAsync(targetMagNo);

                if (success)
                {
                    await RefreshHmesInfoAsync();
                    _loggingService.LogInformation($"Tìm kiếm và chọn Magazine No '{targetMagNo}' thành công trên HMES Web.");
                }
                else
                {
                    ShowWarningMessage($"Không thể tìm kiếm Magazine No '{targetMagNo}'. Vui lòng kiểm tra trang HMES Web.");
                    _loggingService.LogWarning($"Tìm kiếm Magazine No '{targetMagNo}' thất bại.");
                }
            }
            catch (Exception ex)
            {
                ShowWarningMessage($"Gửi Magazine No lỗi: {ex.Message}");
                _loggingService.LogError($"Gửi Magazine No error: {ex.Message}");
            }
            finally
            {
                IsDataSending = false;
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
            IScanDataService scanDataService)
        {
            _loggingService = loggingService;
            _configService = configService;
            _appState = appState;
            _resultService = scanResultService;
            _blockRFService = blockRFService;
            _audioNotificationService = audioNotificationService;
            _shopFloorService = shopFloorService;
            _scanDataService = scanDataService;

            _hmesRefreshTimer.Interval = TimeSpan.FromSeconds(2);
            _hmesRefreshTimer.Tick += async (_, _) => await RefreshHmesInfoFromTimerAsync();

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

            _scanDataService.StatusChanged += OnDataExcuteStatusChanged;

            IsSkipHmesWeb = _configService.Config.SkipHmesWeb;
            UpdateRunModes();

            LoadSavedMagazines();
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
                    while (reader.TryRead(out string? rawData))
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        if (string.IsNullOrWhiteSpace(rawData)) continue;

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
                    _scanQueue.Writer.TryWrite(rawData);
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
            try
            {
                string pid = data?.Trim() ?? string.Empty;

                if (IsManualMagazineInputFocused)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => InputMagazineNo = pid);
                    _loggingService.LogInformation($"[Scanner] Đã nhận '{pid}' vào ô Magazine No thủ công.");
                    return;
                }

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

                _audioNotificationService.PlayBeep();
                mainSW.Restart();
                IsMessageOn = false;
                _loggingService.LogInformation($"[Event] Device gửi: {data}");

                PID = pid;

                if (await IsBlockedByRfAsync(pid))
                {
                    return;
                }

                HmesBlockAction hmesBlockAction = CheckHmesBlock(pid);
                if (hmesBlockAction == HmesBlockAction.StopBeforeShopFloor)
                {
                    return;
                }

                if (hmesBlockAction == HmesBlockAction.SkipWebAfterShopFloor)
                {
                    if (IsRescanOnly)
                    {
                        _loggingService.LogWarning($"Bỏ qua gửi web/rescan cho PID {pid} do HMES block trong Rescan only mode.");
                        return;
                    }

                    MarkSkipWebSendAfterShopFloor(pid);
                }

                if (IsRescanOnly)
                {
                    _loggingService.LogInformation($"[Event] {pid} - Rescan only mode, sending directly to web.");
                    await SendRescanOnlyAsync(pid);
                    return;
                }

                if (!ClearShopFloorBeforeNextPidIfNeeded(pid))
                {
                    return;
                }

                await _serialProxyManager.SendToAppAsync(data);
                await ProcessShopFloorResultAfterForwardAsync(data);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Lỗi khi xử lý dữ liệu scanner: {ex.Message}");
            }
            finally
            {
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

            _loggingService.LogInformation($"Check logic done {mainSW.ElapsedMilliseconds}");

            return false;
        }

        private async Task<bool> IsBlockedByRfAsync(string pid)
        {
            RFInfo rFInfo = await _blockRFService.IsBlock(pid);
            if (rFInfo == null)
            {
                _loggingService.LogInformation($"Check RF block done,{mainSW.ElapsedMilliseconds}ms");
                return false;
            }

            ShowWarningMessage($"PID {pid} đã bị chặn do NG RF ở jig {rFInfo.MachineIP} band {rFInfo.Band} sinal{rFInfo.Signpath} antenal number {rFInfo.AntNumber} value {rFInfo.ValueNg}");
            _loggingService.LogWarning($"PID {pid} bị RF block");
            return true;
        }

        private HmesBlockAction CheckHmesBlock(string pid)
        {
            var (isHSMESBlocked, reason) = _scanDataService.IsBlocked(pid);

            if (!isHSMESBlocked)
            {
                _skipWebSendPids.Remove(pid);
                return HmesBlockAction.AllowFullFlow;
            }

            if (IsXrayBlock(reason))
            {
                ShowHmesBlockedMessage(pid, reason);
                return HmesBlockAction.StopBeforeShopFloor;
            }

            ShowWarningMessage($"PID {pid} đã bị block do {reason}");
            _loggingService.LogWarning($"PID {pid} bị HMES block do {reason}");
            return HmesBlockAction.SkipWebAfterShopFloor;
        }

        private void MarkSkipWebSendAfterShopFloor(string pid)
        {
            _skipWebSendPids.Add(pid);
        }

        private void ShowHmesBlockedMessage(string pid, string reason)
        {
            ShowWarningMessage($"PID {pid} đã bị block do {reason} , vui lòng kiểm tra lại");
        }

        private bool ShouldSkipWebSendAfterShopFloor(string pid)
        {
            return _skipWebSendPids.Remove(pid.Trim());
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

        private static bool IsXrayBlock(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            string normalizedReason = reason
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty);

            return normalizedReason.IndexOf("Xray", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task ProcessShopFloorResultAfterForwardAsync(string sentData)
        {
            if (sentData.Contains("CLEAR") || sentData.Contains("TRACE") || IsNone)
            {
                return;
            }

            if (!_shopFloorService.IsScanoutUI()) return;

            bool result = await ReadScanOutResult(sentData);
            if (!result)
            {
                _shouldClearShopFloorBeforeNextPid = true;
                _loggingService.LogWarning($"ShopFloor NG for PID {sentData.Trim()}. CLEAR will be executed before next PID.");
                return;
            }

            if (ShouldSkipWebSendAfterShopFloor(sentData))
            {
                _loggingService.LogWarning($"PID {sentData.Trim()} bị HMES block. Gửi DB HMES nhưng bỏ qua HMES web/rescan.");
                await SendDataExecuteDatabaseOnlyAsync();
                return;
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
            await _scanDataService.ProcessDataAsync(
                workOrder: string.Empty,
                partNo: string.Empty,
                pid: data,
                validateDatabase: false,
                transferToWeb: true);
            await RefreshHmesInfoAsync();
            int currentPCBQty = await _scanDataService.GetPackQty();
            int totalPCBQty = await _scanDataService.GetMagazineQty();

            _loggingService.LogInformation($"Current PCB Qty: {currentPCBQty}, Total PCB Qty: {totalPCBQty}, Magazine Qty: {MagazineQty}, {mainSW.ElapsedMilliseconds}ms");
            if (MagazineQty != 0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
            {
                if (MagazineQty > totalPCBQty && currentPCBQty >= (totalPCBQty - 1))
                    ShowErrorDialog("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt");
                await _scanDataService.PrintManual();
            }
        }

        private void ShowWarningMessage(string message)
        {
            _audioNotificationService.PlayNg();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                InformationMessage = message;
                IsMessageOn = true;
            });
        }

        private void StopSerialProxy()
        {
            // Dừng proxy
            _serialProxyManager?.Stop();
        }

        private async Task<bool> InitScanOutUIAndReadAsync()
        {
            try
            {
                await _shopFloorService.InitializeAsync();
                if (!_shopFloorService.IsScanoutUI())
                {
                    ShowWarningMessage("ShopFloor chưa sẵn sàng. Vui lòng mở đúng màn ScanOut rồi bấm START lại.");
                    _loggingService.LogWarning("ShopFloor UI is not on the ScanOut screen after initialization.");
                    return false;
                }
            }
            catch (Exception ex)
            {
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
                _loggingService.LogError($"Failed to read initial ShopFloor UI data: {ex.Message}");
                return false;
            }

            return true;
        }

        private async Task<bool> StartDataExecuteSessionAsync()
        {
            try
            {
                bool success = await _scanDataService.StartSessionAsync();

                if (!success)
                {
                    //MessageBox.Show("Failed to start session.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _loggingService.LogError("Failed to start data execute session.");
                }

                return success;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to start data execute session: {ex.Message}");
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

        private async Task<bool> SendDataExecuteAsync()
        {
            bool finalResult = false;
            if (IsInChooseEBRMode) return false;
            
            try
            {
                IsDataSending = true;               

                _loggingService.LogInformation($"Sending data to database: WorkOrder={WorkOrder}, ModelSuffix={PartNo}, WipSerialNumber={PID}");
                ProcessResult result = new ProcessResult();
                if(IsScanOutOnly || IsSkipHmesWeb)
                {
                    result =  await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: false);                    
                }
                else if (IsRescanOnly)
                {
                    result = await _scanDataService.ProcessDataAsync(
                        workOrder: string.Empty,
                        partNo: string.Empty,
                        pid: PID,
                        validateDatabase: false,
                        transferToWeb: true);
                    await RefreshHmesInfoAsync();
                    int currentPCBQty = await _scanDataService.GetPackQty();
                    int totalPCBQty = await _scanDataService.GetMagazineQty();

                    _loggingService.LogInformation($"Current PCB Qty: {currentPCBQty}, Total PCB Qty: {totalPCBQty}, Magazine Qty: {MagazineQty}");
                    if (MagazineQty != 0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
                    {
                        if (MagazineQty > totalPCBQty && currentPCBQty >= (totalPCBQty - 1))
                            ShowErrorDialog("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt");
                        await _scanDataService.PrintManual();
                    }
                }
                else
                {
                    if(_configService.Config.IsWOMode)
                    {
                        result = await SendInWOMode();
                    }
                    else
                    {
                        result = await SendInEBRMode();
                    }                    
                }


                if (!result.Success)
                {
                    // 실패 시 메시지 표시
                    if (!string.IsNullOrEmpty(result.ValidationResult))
                    {
                        //MessageBox.Show(result.ValidationResult, "Data Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _loggingService.LogError($"Validation Error: {result.ValidationResult}");
                    }
                    else
                    {
                        //MessageBox.Show(result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _loggingService.LogError($"Error: {result.Message}");
                    }

                }
                else
                {
                    finalResult = true;
                }

                if (!IsScanOutOnly && !IsSkipHmesWeb)
                {
                    await RefreshHmesInfoAsync();
                }

                // Dashboard keeps the last ShopFloor values visible for trace/debug.
                if (_scanDataService.ShouldClearAfterSend)
                {
                    _loggingService.LogInformation("Auto clear after send is enabled, but ShopFloor display values are kept on dashboard.");
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                //MessageBox.Show("NG", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _loggingService.LogError($"Error sending data: {ex.Message}");

            }
            finally
            {
                IsDataSending = false;
            }
            return finalResult;
        }

        private async Task<bool> SendDataExecuteDatabaseOnlyAsync()
        {
            if (IsInChooseEBRMode)
            {
                return false;
            }

            try
            {
                IsDataSending = true;
                _loggingService.LogInformation($"Sending database-only HMES data: WorkOrder={WorkOrder}, ModelSuffix={PartNo}, WipSerialNumber={PID}");

                ProcessResult result = await _scanDataService.ProcessDataAsync(
                    WorkOrder,
                    PartNo,
                    PID,
                    validateDatabase: true,
                    transferToWeb: false);

                if (!result.Success)
                {
                    if (!string.IsNullOrEmpty(result.ValidationResult))
                    {
                        _loggingService.LogError($"Validation Error: {result.ValidationResult}");
                    }
                    else
                    {
                        _loggingService.LogError($"Database-only HMES error: {result.Message}");
                    }

                    return false;
                }

                _loggingService.LogInformation($"Database-only HMES send successful for PID {PID}.");
                return true;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                _loggingService.LogError($"Error sending database-only HMES data: {ex.Message}");
                return false;
            }
            finally
            {
                IsDataSending = false;
            }
        }

        private async Task<ProcessResult> SendInWOMode()
        {            
            ProcessResult result = new ProcessResult();
            if (string.IsNullOrEmpty(WorkOrder)) return result;
            bool transferToWeb = !IsSkipHmesWeb && !IsScanOutOnly;
            if (string.IsNullOrEmpty(SelectedEBR))
            {
                ShowErrorDialog("Vui lòng chọn Part No trước khi scan");
                result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: false);
            }
            else
            {
                if (WorkOrder != SelectedEBR)
                {
                    _loggingService.LogInformation($"PartNo miss match: {WorkOrder} != {SelectedEBR}");
                    result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: false);
                }
                else
                {
                    result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: transferToWeb);
                }
            }
            return result;
        }

        private async Task<ProcessResult> SendInEBRMode()
        {
            ProcessResult result = new ProcessResult();
            if(string.IsNullOrEmpty(PartNo)) return result;
            bool transferToWeb = !IsSkipHmesWeb && !IsScanOutOnly;
            if (PartNo != SelectedEBR && !string.IsNullOrEmpty(SelectedEBR))
            {
                _loggingService.LogInformation($"PartNo miss match: {PartNo} != {SelectedEBR}");
                int PackQty = IsSkipHmesWeb ? 0 : await _scanDataService.GetPackQty();
                if (PackQty == 0)
                {                    
                    result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: false);
                }
                else
                {
                    result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: transferToWeb);
                }
            }
            else
            {
                result = await _scanDataService.ProcessDataAsync(WorkOrder, PartNo, PID, validateDatabase: true, transferToWeb: transferToWeb);
            }

            if (!IsSkipHmesWeb)
            {
                int currentPCBQty = await _scanDataService.GetPackQty();
                int totalPCBQty = await _scanDataService.GetMagazineQty();

                if(MagazineQty !=0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
                {
                    if(MagazineQty>totalPCBQty && currentPCBQty >= (totalPCBQty-1)) 
                        ShowErrorDialog("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt");
                    await _scanDataService.PrintManual();
                }
            }

            return result;
        }



        public async Task<bool> ReadScanOutResult(string pid)
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

                return scanResult.IsOk;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to read scan out result: {ex.Message}");
                return false;
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

        private async Task RefreshHmesInfoAsync()
        {
            try
            {
                RescanInfo info = await _scanDataService.GetLastRescanInfo();
                HmesResult = info.Result;
                HmesMessage = info.Message;
                HmesPackQty = info.PackQty;
                HmesMagazineQty = info.MagazineQty;
                HmesMagazineNo = info.MagazineNo;

                if (!string.IsNullOrEmpty(info.TransactionType))
                {
                    if (info.TransactionType == "NORMAL" && !IsTransactionTypeNormal)
                    {
                        IsTransactionTypeNormal = true;
                    }
                    else if (info.TransactionType == "CANCEL" && !IsTransactionTypeCancel)
                    {
                        IsTransactionTypeCancel = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to refresh HMES info: {ex.Message}");
            }
        }

        private void StartHmesRefreshTimer()
        {
            if (!_hmesRefreshTimer.IsEnabled)
            {
                _hmesRefreshTimer.Start();
            }
        }

        private void StopHmesRefreshTimer()
        {
            if (_hmesRefreshTimer.IsEnabled)
            {
                _hmesRefreshTimer.Stop();
            }

            _isRefreshingHmesInfo = false;
        }

        private async Task RefreshHmesInfoFromTimerAsync()
        {
            if (!IsStarted || IsDataSending || _isRefreshingHmesInfo)
            {
                return;
            }

            try
            {
                _isRefreshingHmesInfo = true;
                await RefreshHmesInfoAsync();
            }
            finally
            {
                _isRefreshingHmesInfo = false;
            }
        }

        private static void ShowErrorDialog(string message)
        {
            MessageBox.Show(Application.Current.MainWindow, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnDataExcuteStatusChanged(object sender, string status)
        {
            //MessageBox.Show(status);
            _loggingService.LogInformation(status);
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
