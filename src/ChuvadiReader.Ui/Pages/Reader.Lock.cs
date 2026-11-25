using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Microsoft.AspNetCore.Components.Web;

namespace ChuvadiReader.Ui.Pages;

/// <summary>
/// D-207 / D-210 on the Reader page: the card that asks for a password a change needs to keep the file's lock, the
/// check before marking a file whose permissions forbid changes, and keeping the source as it was when the work
/// began (so a save never lands on a file that changed underneath it).
/// </summary>
public partial class Reader
{
    private LockNeed? _lockNeed;
    private string? _lockMsg;
    private string? _lockPw;
    private string? _lockErr;
    private bool _lockBusy;
    private Func<Task>? _lockThen;
    private Func<Task>? _lockSaveAs;

    /// <summary>The last save stopped because the source moved or changed: the Marks card offers Save as.</summary>
    private bool _mkOfferSaveAs;

    private const string ChangedBeforeWork =
        "This file changed on disk after you opened it. Close it and open it again to mark the version that's there now.";

    private void AskLock(LockNeed need, string message, Func<Task> then, Func<Task>? saveAs)
    {
        _lockNeed = need;
        _lockMsg = message;
        _lockPw = null;
        _lockErr = null;
        _lockThen = then;
        _lockSaveAs = saveAs;
    }

    private void CancelLock()
    {
        _lockNeed = null;
        _lockMsg = null;
        _lockPw = null;
        _lockErr = null;
        _lockThen = null;
        _lockSaveAs = null;
    }

    private async Task SubmitLockAsync()
    {
        if (Tab is not { } tab || _lockNeed is not { } need || string.IsNullOrEmpty(_lockPw) || _lockBusy) return;
        _lockBusy = true;
        StateHasChanged();
        try
        {
            var pw = _lockPw!;
            var err = await Task.Run(() => need == LockNeed.OwnerPassword ? TabSave.GiveOwnerPassword(tab, pw) : TabSave.GiveOpenPassword(tab, pw));
            if (err is not null)
            {
                _lockErr = err;
                return;
            }

            var then = _lockThen;
            CancelLock();
            if (then is not null) await then();
        }
        catch (Exception ex)
        {
            _lockErr = ex.Message;
        }
        finally
        {
            _lockBusy = false;
            StateHasChanged();
        }
    }

    private async Task LockSaveAs()
    {
        var saveAs = _lockSaveAs;
        CancelLock();
        if (saveAs is not null) await saveAs();
    }

    private async Task LockKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SubmitLockAsync();
        else if (e.Key == "Escape") CancelLock();
    }

    /// <summary>Mark was turned on: ask for the owner password first when the file forbids changes (D-207), and
    /// keep the source as it is now for the work (D-210).</summary>
    private async Task BeginMarkWorkAsync()
    {
        if (Tab is not { } tab || tab.Path is null) return;
        try
        {
            if (await Task.Run(() => TabSave.NeedsOwnerToChange(tab)))
            {
                _markup = false;
                AskLock(LockNeed.OwnerPassword, SourceLock.OwnerNeededToChange, () =>
                {
                    if (!_markup) ToggleMarkup();
                    return Task.CompletedTask;
                }, null);
                return;
            }

            await Task.Run(() => TabSave.BeginWork(tab));
        }
        catch (SourceChangedException)
        {
            _markup = false;
            ShowToast(ChangedBeforeWork);
        }
        catch (Exception ex)
        {
            _markup = false;
            ShowToast(ex.Message);
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>A save stopped: make sure the reason is on screen (BUG-23, MK30: a selected mark's card used to hide
    /// the Marks card that carries it, so the save looked silent).</summary>
    private void ShowMarkSaveError(string message, bool offerSaveAs)
    {
        _mkStatus = message;
        _mkError = true;
        _mkOfferSaveAs = offerSaveAs;
        _selText = null; _selShape = null; _selImage = null;
        _sigOpen = false;
    }
}
