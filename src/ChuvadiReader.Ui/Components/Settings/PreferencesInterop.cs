using ChuvadiReader.Core.Storage;
using ChuvadiReader.Core.Theme;
using Microsoft.JSInterop;

namespace ChuvadiReader.Ui.Components.Settings;

/// <summary>
/// Applies stored preferences to the document through <c>window.chuvadiTheme</c> (theme.js).
/// Core stays JS-free; this is the UI-side bridge. Every call swallows interop failures
/// (prerender / disposed circuit) because a missed visual preference must never break a page.
/// </summary>
public static class PreferencesInterop
{
    /// <summary>Apply UI scale and reduce motion (not the theme — the shell owns that).</summary>
    public static async Task ApplyAsync(IJSRuntime js, PreferencesService prefs)
    {
        try
        {
            await js.InvokeVoidAsync("chuvadiTheme.setScale", prefs.UiScale);
            await js.InvokeVoidAsync("chuvadiTheme.setReducedMotion", prefs.ReduceMotion);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    /// <summary>Apply a theme by its <c>data-theme</c> key (light, dark, palm, indigo, contrast).</summary>
    public static async Task ApplyThemeAsync(IJSRuntime js, ChuvadiTheme theme)
    {
        try
        {
            await js.InvokeVoidAsync("chuvadiTheme.set", theme.Key());
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }
}
