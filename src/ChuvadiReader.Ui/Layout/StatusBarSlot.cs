namespace ChuvadiReader.Ui.Layout;

/// <summary>
/// One button in the status bar that the page in view owns (D-191: the encoding a text file was read as). The layout
/// hands this to its pages as a cascading value and draws the button; a page fills it and clears it when it goes.
/// It lives with the layout, so each window or browser session has its own.
/// </summary>
public sealed class StatusBarSlot
{
    /// <summary>The button's text; null when there is no button.</summary>
    public string? Label { get; private set; }

    /// <summary>Plain text shown after the button, e.g. "37 bytes don't fit UTF-8"; null for none.</summary>
    public string? Note { get; private set; }

    /// <summary>The button's tooltip and accessible name.</summary>
    public string? Title { get; private set; }

    public Func<Task>? Run { get; private set; }

    public event Action? Changed;

    public void Set(string label, string? note, string title, Func<Task> run)
    {
        Run = run; // a fresh delegate on every render; not a change the layout needs to draw
        if (label == Label && note == Note && title == Title) return;
        (Label, Note, Title) = (label, note, title);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Run = null;
        if (Label is null && Note is null) return;
        (Label, Note, Title) = (null, null, null);
        Changed?.Invoke();
    }
}
