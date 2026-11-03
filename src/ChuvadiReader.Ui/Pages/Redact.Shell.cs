using ChuvadiReader.Core.Reader;
using Microsoft.AspNetCore.Components;

namespace ChuvadiReader.Ui.Pages;

/// <summary>D-10 wiring for Protect's redaction view: rails, counter, status bar, Ctrl K.</summary>
public partial class Redact
{
    [Inject] private ShellService Shell { get; set; } = default!;
    [Inject] private TabsService Tabs { get; set; } = default!;

    private void ShellInit()
    {
        Shell.SetMode(DocMode.Protect, Tabs.Active?.Id);
        Shell.SetDocumentMenu(null);
        _counterHandler = p => InvokeAsync(() => GoTo(p));
        _findHandler = () => InvokeAsync(() => { _findOpen = true; StateHasChanged(); });
        Shell.CounterHandler = _counterHandler;
        Shell.FindHandler = _findHandler;
        _findTextHandler = q => InvokeAsync(async () =>
        {
            _findOpen = true;
            _query = q;
            StateHasChanged();
            await RunSearch();
            StateHasChanged();
        });
        Shell.FindTextHandler = _findTextHandler;
    }

    private void ShellPublish()
    {
        if (_session is { } s)
        {
            Shell.SetCounter(_curPage, s.Document.PageCount, "page");
            Shell.SetCounterStep(1);
            Shell.SetActivity($"Protect · redacting {_name} · {RedactionCount} box{(RedactionCount == 1 ? "" : "es")}{(AcceptedMatchCount > 0 ? $" · {AcceptedMatchCount} found" : "")} · always writes a new file");
        }
        else
        {
            Shell.ClearCounter();
            Shell.SetActivity("Protect · redact");
        }

        Shell.RegisterCommands(this, new List<ShellCommand>
        {
            new("rd.box", "Redact box (draw on the page)", "Protect", "redact", "R", () => { _findOpen = false; StateHasChanged(); return Task.CompletedTask; }),
            new("rd.find", "Find & redact", "Protect", "find-in-page", null, () => { _findOpen = true; StateHasChanged(); return Task.CompletedTask; }, _session is null ? "Open a document first" : null),
            new("rd.save", "Save redacted copy", "Protect", "download", null, Save, HasAnythingToRedact ? null : "Nothing to redact yet"),
            new("rd.clear", "Clear all redaction boxes", "Protect", "close", null, () => { ClearAll(); StateHasChanged(); return Task.CompletedTask; }, HasAnythingToRedact ? null : "Nothing to clear"),
            new("rd.fetch", "Redact another file…", "Protect", "folder", null, Fetch),
            new("rd.zoomin", "Zoom in", "View", "zoom-in", null, () => { ZoomIn(); StateHasChanged(); return Task.CompletedTask; }),
            new("rd.zoomout", "Zoom out", "View", "zoom-out", null, () => { ZoomOut(); StateHasChanged(); return Task.CompletedTask; }),
        });
    }

    private Func<int, Task>? _counterHandler;
    private Func<Task>? _findHandler;
    private Func<string, Task>? _findTextHandler;

    private void ShellRelease()
    {
        if (Shell.CounterHandler == _counterHandler)
        {
            Shell.CounterHandler = null;
        }

        if (Shell.FindHandler == _findHandler)
        {
            Shell.FindHandler = null;
        }

        if (Shell.FindTextHandler == _findTextHandler)
        {
            Shell.FindTextHandler = null;
        }

        Shell.UnregisterCommands(this);
    }

    /// <summary>Stamp, Watermark, Flatten, Password and certificate signing act on the open
    /// tab's file, so they live in the Reader's Protect view.</summary>
    private void BackToProtect(string tool)
    {
        Shell.PendingTool = tool;
        Shell.SetMode(DocMode.Protect, Tabs.Active?.Id);
        Nav.NavigateTo("/reader");
    }
}
