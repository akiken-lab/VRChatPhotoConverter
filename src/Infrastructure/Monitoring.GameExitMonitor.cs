using System.Diagnostics;
using System.Management;
using Core.Abstractions;
using Core.Models;

namespace Infrastructure.Monitoring;

public sealed class GameExitMonitor : IGameExitMonitor
{
    private readonly object _lock = new();
    private readonly HashSet<string> _targetExeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _knownProcesses = new();
    private ManagementEventWatcher? _stopWatcher;
    private Timer? _pollingTimer;

    public event EventHandler<string>? GameExited;

    public bool IsRunning { get; private set; }

    public void Start(IEnumerable<WatchTarget> targets)
    {
        lock (_lock)
        {
            Stop();

            _targetExeNames.Clear();
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.ExeName))
                {
                    continue;
                }

                _targetExeNames.Add(Path.GetFileName(target.ExeName));
            }

            if (_targetExeNames.Count == 0)
            {
                IsRunning = false;
                return;
            }

            if (TryStartWmiWatcher())
            {
                IsRunning = true;
                return;
            }

            StartPollingWatcher();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_stopWatcher is not null)
            {
                _stopWatcher.EventArrived -= StopWatcherOnEventArrived;
                _stopWatcher.Stop();
                _stopWatcher.Dispose();
                _stopWatcher = null;
            }

            if (_pollingTimer is not null)
            {
                _pollingTimer.Dispose();
                _pollingTimer = null;
            }

            _knownProcesses.Clear();
            IsRunning = false;
        }
    }

    private bool TryStartWmiWatcher()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
            _stopWatcher = new ManagementEventWatcher(query);
            _stopWatcher.EventArrived += StopWatcherOnEventArrived;
            _stopWatcher.Start();
            return true;
        }
        catch (ManagementException)
        {
            if (_stopWatcher is not null)
            {
                _stopWatcher.EventArrived -= StopWatcherOnEventArrived;
                _stopWatcher.Dispose();
                _stopWatcher = null;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            if (_stopWatcher is not null)
            {
                _stopWatcher.EventArrived -= StopWatcherOnEventArrived;
                _stopWatcher.Dispose();
                _stopWatcher = null;
            }

            return false;
        }
    }

    private void StartPollingWatcher()
    {
        SnapshotCurrentProcesses(_knownProcesses);
        _pollingTimer = new Timer(PollingTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void PollingTick(object? state)
    {
        List<string> exitedNames;
        lock (_lock)
        {
            if (_pollingTimer is null)
            {
                return;
            }

            var current = new Dictionary<int, string>();
            SnapshotCurrentProcesses(current);

            exitedNames = _knownProcesses
                .Where(kv => !current.ContainsKey(kv.Key))
                .Select(kv => kv.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _knownProcesses.Clear();
            foreach (var kv in current)
            {
                _knownProcesses[kv.Key] = kv.Value;
            }
        }

        foreach (var name in exitedNames)
        {
            GameExited?.Invoke(this, name);
        }
    }

    private void SnapshotCurrentProcesses(IDictionary<int, string> output)
    {
        output.Clear();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processName = Path.GetFileName(process.ProcessName + ".exe");
                if (_targetExeNames.Contains(processName))
                {
                    output[process.Id] = processName;
                }
            }
            catch
            {
                // Ignore transient process access errors.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void StopWatcherOnEventArrived(object sender, EventArrivedEventArgs e)
    {
        var processName = (e.NewEvent.Properties["ProcessName"]?.Value as string) ?? string.Empty;
        processName = Path.GetFileName(processName);

        if (_targetExeNames.Contains(processName))
        {
            GameExited?.Invoke(this, processName);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
