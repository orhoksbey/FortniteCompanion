using System.Diagnostics;

namespace FortniteCompanion.Core.Services;

/// <summary>
/// Fortnite'ın çalışıp çalışmadığını yalnızca işlem listesine bakarak anlar (Görev Yöneticisi'nin yaptığı gibi).
/// Oyun işlemine bağlanmaz, belleğini okumaz, tutamaç açmaz.
/// </summary>
public sealed class FortniteProcessMonitor : IDisposable
{
    private static readonly string[] ProcessNames = { "FortniteClient-Win64-Shipping" };
    private readonly Timer _timer;
    private bool? _running;

    public event Action<bool>? RunningChanged;

    public bool IsRunning => _running == true;

    public FortniteProcessMonitor()
    {
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    private void Check()
    {
        bool running;
        try
        {
            running = false;
            foreach (var name in ProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                running |= procs.Length > 0;
                foreach (var p in procs)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            return;
        }

        if (_running != running)
        {
            _running = running;
            RunningChanged?.Invoke(running);
        }
    }

    public void Dispose() => _timer.Dispose();
}
