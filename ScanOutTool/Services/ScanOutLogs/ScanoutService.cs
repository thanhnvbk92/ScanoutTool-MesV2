using ControlzEx.Standard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class ScanoutService:IScanoutService
    {
        private readonly string _eventFile;
        private readonly string _dataFile;
        private readonly string _debugFile;
        private readonly CancellationTokenSource _cts = new();
        private Task _monitorTask;
        private readonly ConcurrentQueue<string> _pidQueue = new();

        public event Action<string , string , string , string > OnResultReady;

        public ScanoutService(string eventFile, string dataFile, string debugFile)
        {
            _eventFile = eventFile;
            _dataFile = dataFile;
            _debugFile = debugFile;
        }

        public void RequestResult(string pid)
        {
            _pidQueue.Enqueue(pid);
        }

        public void Start()
        {
            _monitorTask = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_pidQueue.TryDequeue(out var pid))
                    {
                        string model = null, wo = null;
                        string result = "OK";

                        try
                        {
                            var eventLines = File.ReadAllLines(_eventFile);
                            foreach (var line in eventLines)
                            {
                                if (line.Contains(pid) && line.Contains("Model Change"))
                                    model = Regex.Match(line, @"NEW : ([\w\-.]+)").Groups[1].Value;
                                if (line.Contains(pid) && line.Contains("WorkOrder Change"))
                                    wo = Regex.Match(line, @"NEW : ([\w\-]+)").Groups[1].Value;
                            }

                            // fallback nếu không gắn kèm pid trong dòng
                            if (model == null || wo == null)
                            {
                                string lastModel = null, lastWo = null;
                                foreach (var line in eventLines.Reverse())
                                {
                                    if (lastModel == null && line.Contains("Model Change"))
                                        lastModel = Regex.Match(line, @"NEW : ([\w\-.]+)").Groups[1].Value;
                                    if (lastWo == null && line.Contains("WorkOrder Change"))
                                        lastWo = Regex.Match(line, @"NEW : ([\w\-]+)").Groups[1].Value;
                                    if (lastModel != null && lastWo != null) break;
                                }
                                model ??= lastModel;
                                wo ??= lastWo;
                            }

                            if (File.ReadAllText(_debugFile).Contains(pid))
                                result = "NG";
                            else if (!File.ReadAllText(_dataFile).Contains(pid))
                                result = "OK";

                            OnResultReady?.Invoke(pid, model ?? "", wo ?? "", result);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Processing PID {pid}: {ex.Message}");
                        }
                    }

                    await Task.Delay(100);
                }
            });
        }

        public void Stop()
        {
            _cts.Cancel();
            _monitorTask?.Wait();
        }

    }
}
