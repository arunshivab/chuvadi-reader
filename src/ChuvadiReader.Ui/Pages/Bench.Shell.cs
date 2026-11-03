using ChuvadiReader.Core.Reader;
using Microsoft.AspNetCore.Components;

namespace ChuvadiReader.Ui.Pages;

/// <summary>D-10 wiring for Arrange (the Bench view): rails, mode, status bar and Ctrl K.</summary>
public partial class Bench
{
    [Inject] private ShellService Shell { get; set; } = default!;
    [Inject] private TabsService Tabs { get; set; } = default!;

    private bool _shelfHidden;
    private bool _shellHooked;

    /// <summary>The desk that rail actions act on: the one holding the selection, else the first.</summary>
    private Guid? FocusDeskId
    {
        get
        {
            foreach (var d in BenchSvc.Desks)
            {
                if (d.Pages.Any(p => BenchSvc.IsSelected(p.Id)))
                {
                    return d.Id;
                }
            }

            return BenchSvc.Desks.FirstOrDefault()?.Id;
        }
    }

    private void AddBlankToFocused()
    {
        if (FocusDeskId is { } id)
        {
            AddBlank(id);
        }
    }

    private Task CyclePerRow() => SetPerRow(_perRow % 3 + 1);

    private void ShellPublish()
    {
        if (!_shellHooked)
        {
            _shellHooked = true;
            Shell.SetMode(DocMode.Arrange, Tabs.Active?.Id);
            Shell.ClearCounter();
            Shell.SetDocumentMenu(null);
        }

        var pages = BenchSvc.Desks.Sum(d => d.Pages.Count);
        var desks = BenchSvc.Desks.Count;
        var sel = BenchSvc.SelectedCount;
        Shell.SetActivity($"Arrange · {desks} desk{(desks == 1 ? "" : "s")} · {pages} page{(pages == 1 ? "" : "s")}{(sel > 0 ? $" · {sel} selected" : "")}");
        Shell.RegisterCommands(this, ArrangeCommands().ToList());
    }

    private void ShellRelease()
    {
        Shell.UnregisterCommands(this);
    }

    private IEnumerable<ShellCommand> ArrangeCommands()
    {
        Func<Task> T(Action a) => () => { a(); StateHasChanged(); return Task.CompletedTask; };
        var empty = BenchSvc.IsEmpty ? "The bench is empty" : null;
        yield return new("a.fetch", "Fetch a file onto the shelf", "Arrange", "folder", null, FetchFile);
        yield return new("a.desk", "Add desk", "Arrange", "plus", null, T(AddDesk), BenchSvc.CanAddDesk ? null : "30-desk limit reached");
        yield return new("a.blank", "Add a blank page", "Arrange", "page-add", null, T(AddBlankToFocused), FocusDeskId is null ? "No desk yet" : null);
        yield return new("a.shelf", _shelfHidden ? "Show the shelf" : "Hide the shelf", "Arrange", "shelf", null, T(() => _shelfHidden = !_shelfHidden));
        yield return new("a.session", "Save or open a bench session", "Arrange", "save", null, T(ToggleSessionMenu));
        yield return new("a.reset", "Reset bench", "Arrange", "close", null, T(ConfirmReset));
        yield return new("a.extract", "Move selected pages to a new desk", "Arrange", "page-extract", null, T(ExtractSelected), BenchSvc.SelectedCount == 0 ? "Select pages first" : null);
        yield return new("a.compare", "Compare the two selected pages", "Arrange", "compare", null, OpenCompare, BenchSvc.SelectedCount == 2 ? null : "Select exactly two pages");
        yield return new("a.combine", "Combine every desk into one PDF", "Arrange", "doc-merge", null, CombineAll, empty, "merge");
        yield return new("a.bind", "Bind all desks", "Arrange", "doc-merge", null, BindAll, empty, "save export");
        for (var n = 1; n <= 3; n++)
        {
            var nn = n;
            yield return new("a.row" + n, $"{n} desk{(n == 1 ? "" : "s")} per row", "View", nn == 1 ? "page-single" : nn == 2 ? "page-facing" : "grid-view", null, () => SetPerRow(nn));
        }

        yield return new("a.merge", "Merge files…", "Tools", "merge", null, T(() => Nav.NavigateTo("/merge")));
        yield return new("a.split", "Split pages…", "Tools", "split", null, T(() => Nav.NavigateTo("/split")));
    }
}
