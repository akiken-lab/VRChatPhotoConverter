using Core.Models;

namespace Core.Abstractions;

public interface IGameExitMonitor : IDisposable
{
    event EventHandler<string>? GameExited;
    bool IsRunning { get; }
    void Start(IEnumerable<WatchTarget> targets);
    void Stop();
}
