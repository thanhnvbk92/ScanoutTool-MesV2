using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScanOutLogLib.Models;

namespace ScanOutLogLib.Services
{
    public interface IScanResultService : IDisposable
    {
        void Start(string logRoot);
        Task<ScanResult> RequestResultAsync(string pid, int timeoutMs);
        event Action<string> OnLog;
    }

    public class ScanResultService : IScanResultService
    {
        private string _logRoot;
        private FileSystemWatcher _folderWatcher;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ScanResult>> _waiters = new();
        private readonly ConcurrentDictionary<string, ScanResult> _cache = new();
        private readonly ConcurrentDictionary<string, DateTime> _pendingPids = new();
        
        private readonly List<HybridTailWatcher> _watchers = new();
        private string _eventPath, _debugPath, _dataPath;

        private readonly StringBuilder _debugBuffer = new();
        private readonly StringBuilder _dataBuffer = new();
        private DateTime _debugTime, _dataTime;

        public event Action<string> OnLog;

        public void Start(string logRoot)
        {
            if (!Directory.Exists(logRoot)) throw new DirectoryNotFoundException("Invalid log path: " + logRoot);
            _logRoot = logRoot;

            UpdateWatchers();

            _folderWatcher = new FileSystemWatcher(logRoot, "*.log") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            _folderWatcher.Created += (s, e) => UpdateWatchers();
            _folderWatcher.Renamed += (s, e) => UpdateWatchers();
        }

        private void UpdateWatchers()
        {
            var eventFile = FindLatest("EVENT");
            var debugFile = FindLatest("DEBUG");
            var dataFile = FindLatest("DATA");

            if (eventFile != _eventPath) ReplaceWatcher(ref _eventPath, eventFile, line => HandleEventLine(line));
            if (debugFile != _debugPath) ReplaceWatcher(ref _debugPath, debugFile, line => HandleProcessingLine(line, true));
            if (dataFile != _dataPath) ReplaceWatcher(ref _dataPath, dataFile, line => HandleProcessingLine(line, false));
        }

        private string FindLatest(string type) => 
            Directory.GetFiles(_logRoot, $"LGE.SFC.MainFrame.exe_{type}_*.log", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();

        private void ReplaceWatcher(ref string oldPath, string newPath, Action<string> handler)
        {
            string currentPath = oldPath;
            var old = _watchers.FirstOrDefault(w => w.FilePath == currentPath);
            if (old != null) { old.Stop(); _watchers.Remove(old); }
            
            if (!string.IsNullOrEmpty(newPath))
            {
                var w = new HybridTailWatcher(newPath);
                w.OnNewLine += handler;
                w.Start();
                _watchers.Add(w);
                oldPath = newPath;
            }
        }

        public async Task<ScanResult> RequestResultAsync(string pid, int timeoutMs)
        {
            if (_cache.TryRemove(pid, out var cached)) return SupplementResult(cached);

            var tcs = new TaskCompletionSource<ScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[pid] = tcs;

            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => { if (_waiters.TryRemove(pid, out var t)) t.TrySetResult(new ScanResult { PID = pid, Result = "TIMEOUT" }); });

            var result = await tcs.Task;
            return SupplementResult(result);
        }

        private ScanResult SupplementResult(ScanResult res)
        {
            if (string.IsNullOrEmpty(_eventPath)) return res;
            
            string marker = $": SERIAL SCAN:{res.PID}";
            string wo = null, model = null;

            foreach (var line in ReadLinesAfter(_eventPath, marker, 100))
            {
                if (wo == null && line.Contains("WorkOrder :")) wo = line.Split("WorkOrder :")[1].Trim().Split(' ')[0];
                if (model == null && line.Contains("Model Change") && line.Contains("NEW : ")) model = line.Substring(line.IndexOf("NEW : ") + 6).Trim().Split(' ')[0];
                if (wo != null && model != null) break;
            }
            res.WorkOrder = wo;
            res.Model = model;
            return res;
        }

        private void HandleEventLine(string line)
        {
            var match = Regex.Match(line, @"SERIAL SCAN:(\w+)");
            if (match.Success)
            {
                var pid = match.Groups[1].Value;
                if (TryParseTime(line, out var time)) _pendingPids[pid] = time;
            }
        }

        private void HandleProcessingLine(string line, bool isDebug)
        {
            var buffer = isDebug ? _debugBuffer : _dataBuffer;
            ref DateTime timeRef = ref isDebug ? ref _debugTime : ref _dataTime;

            if (line.Contains("ExecuteService()")) { buffer.Clear(); TryParseTime(line, out timeRef); }
            buffer.AppendLine(line);

            if (line.Contains("</NewDataSet>"))
            {
                string xml = buffer.ToString();
                foreach (var pid in _pendingPids.Keys.ToList())
                {
                    if (_pendingPids.TryGetValue(pid, out var sentTime) && timeRef >= sentTime && xml.Contains($"<LOTID>{pid}</LOTID>"))
                    {
                        var res = new ScanResult { PID = pid, Result = isDebug ? "NG" : (xml.Contains("[BR_COR_REG_START_END_LOT_G]") ? "OK" : null) };
                        if (res.Result != null)
                        {
                            _pendingPids.TryRemove(pid, out _);
                            if (_waiters.TryRemove(pid, out var tcs)) tcs.TrySetResult(res);
                            else _cache[pid] = res;
                        }
                    }
                }
            }
        }

        private bool TryParseTime(string line, out DateTime time)
        {
            var m = Regex.Match(line, @"\[(\w{3}-\d{2}-\d{4} \d{2}:\d{2}:\d{2})\]");
            return DateTime.TryParseExact(m.Groups[1].Value, "MMM-dd-yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        private IEnumerable<string> ReadLinesAfter(string path, string marker, int max)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string line; bool found = false; int count = 0;
            while ((line = sr.ReadLine()) != null)
            {
                if (!found) { if (line.Contains(marker)) found = true; continue; }
                yield return line;
                if (++count >= max) break;
            }
        }

        public void Dispose()
        {
            _folderWatcher?.Dispose();
            foreach (var w in _watchers) w.Stop();
        }
    }

    // Simplified HybridTailWatcher inside the same namespace/file or separate if preferred
    public class HybridTailWatcher
    {
        public string FilePath { get; }
        public event Action<string> OnNewLine;
        private long _pos;
        private bool _shouldRead;
        private readonly CancellationTokenSource _cts = new();
        private FileSystemWatcher _fw;

        public HybridTailWatcher(string path) { FilePath = path; _pos = new FileInfo(path).Length; }

        public void Start()
        {
            _fw = new FileSystemWatcher(Path.GetDirectoryName(FilePath), Path.GetFileName(FilePath)) { EnableRaisingEvents = true };
            _fw.Changed += (s, e) => _shouldRead = true;
            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_shouldRead) { _shouldRead = false; ReadNewLines(); }
                    await Task.Delay(50);
                }
            });
        }

        private void ReadNewLines()
        {
            try
            {
                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= _pos) return;
                fs.Seek(_pos, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                while (!sr.EndOfStream) { var line = sr.ReadLine(); if (!string.IsNullOrWhiteSpace(line)) OnNewLine?.Invoke(line); }
                _pos = fs.Position;
            }
            catch { }
        }

        public void Stop() { _cts.Cancel(); _fw?.Dispose(); }
    }
}
