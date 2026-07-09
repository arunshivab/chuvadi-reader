using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Covers the #341 Bench undo/redo history, per-desk select-all / invert, and the
/// identity-preserving clone the history relies on. Uses blank pages so no real PDF or reader
/// session is needed.</summary>
public sealed class BenchHistoryTests
{
    private static BenchService NewBench() =>
        new(new PlaceholderPdfReader(), new BenchComposer(), new ExportService());

    private static (BenchService bench, Desk desk) DeskWithBlanks(int count)
    {
        var bench = NewBench();
        bench.EnsureDesk();
        var desk = bench.Desks[0];
        for (var i = 0; i < count; i++)
        {
            bench.AddBlankToDesk(desk.Id);
        }
        return (bench, desk);
    }

    [Fact]
    public void Undo_RestoresRemovedPage_AndRedoReappliesIt()
    {
        var (bench, desk) = DeskWithBlanks(3);
        Assert.Equal(3, desk.Pages.Count);

        var targetId = desk.Pages[1].Id;
        bench.RemovePage(targetId);
        Assert.Equal(2, bench.Desks[0].Pages.Count);
        Assert.True(bench.CanUndo);

        bench.Undo();
        Assert.Equal(3, bench.Desks[0].Pages.Count);
        Assert.Contains(bench.Desks[0].Pages, p => p.Id == targetId);
        Assert.True(bench.CanRedo);

        bench.Redo();
        Assert.Equal(2, bench.Desks[0].Pages.Count);
        Assert.DoesNotContain(bench.Desks[0].Pages, p => p.Id == targetId);
    }

    [Fact]
    public void Selection_DoesNotConsumeAnUndoStep()
    {
        var (bench, desk) = DeskWithBlanks(2);
        var pageId = desk.Pages[0].Id;

        // A pure selection change must not create a history entry: one Undo should revert the
        // last *edit* (the second blank), not just clear the selection.
        bench.ToggleSelect(pageId);
        Assert.Equal(1, bench.SelectedCount);

        bench.Undo();
        Assert.Single(bench.Desks[0].Pages);
    }

    [Fact]
    public void SelectAll_ThenInvert_TogglesEveryPage()
    {
        var (bench, desk) = DeskWithBlanks(3);

        bench.SelectAllInDesk(desk.Id);
        Assert.Equal(3, bench.SelectedCount);

        bench.InvertSelectionInDesk(desk.Id);
        Assert.Equal(0, bench.SelectedCount);

        bench.InvertSelectionInDesk(desk.Id);
        Assert.Equal(3, bench.SelectedCount);
    }

    [Fact]
    public void Undo_PreservesPageIdentity_AndRevertsCrop()
    {
        var (bench, desk) = DeskWithBlanks(1);
        var pageId = desk.Pages[0].Id;

        bench.SetPageCrop(pageId, 0.1, 0.1, 0.5, 0.5, CropFit.ToSize);
        Assert.True(bench.HasCrop(pageId));

        bench.Undo();
        var page = Assert.Single(bench.Desks[0].Pages);
        Assert.Equal(pageId, page.Id);   // same page object identity survives the snapshot round-trip
        Assert.Null(page.Crop);
    }

    [Fact]
    public void RotateSelected_GoesClockwiseAndCounterClockwise()
    {
        var (bench, desk) = DeskWithBlanks(1);
        var pageId = desk.Pages[0].Id;
        bench.SelectAllInDesk(desk.Id);

        bench.RotateSelected(desk.Id, clockwise: true);
        Assert.Equal(90, bench.Desks[0].Pages[0].Rotation);

        bench.RotateSelected(desk.Id, clockwise: false);
        Assert.Equal(0, bench.Desks[0].Pages[0].Rotation);

        bench.RotateSelected(desk.Id, clockwise: false);
        Assert.Equal(270, bench.Desks[0].Pages[0].Rotation);
    }

    [Fact]
    public void RemoveSelectedAll_ClearsSelectedPagesAcrossTheDesk()
    {
        var (bench, desk) = DeskWithBlanks(3);
        bench.ToggleSelect(desk.Pages[0].Id);
        bench.ToggleSelect(desk.Pages[2].Id);

        bench.RemoveSelectedAll();
        Assert.Single(bench.Desks[0].Pages);
        Assert.Equal(0, bench.SelectedCount);
    }

    [Fact]
    public void DuplicateSelected_CopiesEachSelectedPageInPlace()
    {
        var (bench, desk) = DeskWithBlanks(2);
        bench.ToggleSelect(desk.Pages[0].Id);

        bench.DuplicateSelected(desk.Id);
        Assert.Equal(3, bench.Desks[0].Pages.Count);
    }

    [Fact]
    public void SelectedPair_OnlyWhenExactlyTwoSelected()
    {
        var (bench, desk) = DeskWithBlanks(3);
        Assert.Null(bench.SelectedPair());

        bench.ToggleSelect(desk.Pages[0].Id);
        Assert.Null(bench.SelectedPair());

        bench.ToggleSelect(desk.Pages[1].Id);
        Assert.NotNull(bench.SelectedPair());

        bench.ToggleSelect(desk.Pages[2].Id);
        Assert.Null(bench.SelectedPair());
    }
}
