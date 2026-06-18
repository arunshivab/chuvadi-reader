namespace ChuvadiReader.Core.Lifecycle;

/// <summary>
/// Bridges the Blazor UI and the native host for the splash → main-window
/// handoff. The root component calls <see cref="SignalReady"/> on its first
/// render; the host listens on <see cref="Ready"/> to dismiss the native
/// splash and show the main window. Firing on the real first-render event —
/// not a timer — is what makes "open" feel correct on every machine.
/// </summary>
public interface IAppReadySignal
{
    event Action? Ready;

    void SignalReady();
}

public sealed class AppReadySignal : IAppReadySignal
{
    private bool _fired;

    public event Action? Ready;

    public void SignalReady()
    {
        if (_fired)
        {
            return;
        }

        _fired = true;
        Ready?.Invoke();
    }
}
