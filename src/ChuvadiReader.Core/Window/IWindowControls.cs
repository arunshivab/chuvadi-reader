namespace ChuvadiReader.Core.Window;

/// <summary>
/// Lets the HTML title bar (custom chrome) drive the native window: move it,
/// minimise/maximise/restore, close, and report maximised state so the title
/// bar can show the correct icon.
/// </summary>
public interface IWindowControls
{
    bool IsMaximized { get; }

    event Action? StateChanged;

    void Minimize();

    void ToggleMaximize();

    void Close();

    /// <summary>Begins a native window move (called on title-bar mouse-down).</summary>
    void BeginDrag();
}

/// <summary>Fallback used on hosts without a native window (e.g. future web host).</summary>
public sealed class NoopWindowControls : IWindowControls
{
    public bool IsMaximized => false;

    public event Action? StateChanged
    {
        add { }
        remove { }
    }

    public void Minimize()
    {
    }

    public void ToggleMaximize()
    {
    }

    public void Close()
    {
    }

    public void BeginDrag()
    {
    }
}
