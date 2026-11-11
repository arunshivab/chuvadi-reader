using Xunit;

namespace ChuvadiReader.Hosting.Tests;

/// <summary>
/// Arun's Linux laptop, 11 Nov: a trackpad pinch did nothing. A pinch reaches the page as Ctrl + wheel (WebView2,
/// Chromium), as gesture events (WebKit on Apple systems), or not at all (WebKitGTK, where the host passes it on through
/// window.chuvadiPinch). The shared Ui handles all three, on the PDF view and on the other viewers. The live proof is
/// tools/PixelProof/pinch-zoom.js (Chromium and WebKit).
/// </summary>
public sealed class PinchZoomTests
{
    private static string Ui(params string[] path) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "UiSources", .. path]));

    [Fact]
    public void A_trackpad_pinch_is_taken_however_the_host_delivers_it()
    {
        var js = Ui("wwwroot", "js", "reader.js");
        Assert.Contains("if (!e.ctrlKey) return;", js);                                   // Ctrl + wheel
        Assert.Contains("el.addEventListener('gesturechange', onChange, { passive: false });", js);
        Assert.Contains("window.chuvadiPinch = (phase, scale, x, y) =>", js);             // the host's own gesture
        // The PDF desk and the viewers both register.
        Assert.Contains("attachTrackpadPinch(deskEl, {", js);
        Assert.Contains("attachTrackpadPinch(el, { get: now, set, end, busy: () => !!state.touch });", js);
    }

    [Fact]
    public void A_finger_pinch_is_not_handled_twice()
    {
        // iOS and iPadOS send gesture events for fingers as well; the touch handlers own those.
        var js = Ui("wwwroot", "js", "reader.js");
        Assert.Contains("if (!start || (api.busy && api.busy())) return;", js);
        Assert.Contains("busy: () => pinchActive,", js);
    }

    [Fact]
    public void The_viewers_take_the_pinch_as_their_text_size()
    {
        var razor = Ui("Pages", "Reader.razor");
        Assert.Contains(@"<div class=""rviewer"" @ref=""_viewerHost"" data-zoom=""@_textSize"">", razor);
        Assert.Contains(@"InvokeAsync<bool>(""viewerPinch"", _viewerHost, _ref)", razor);
        Assert.Contains("public void OnViewerPinch(int percent)", razor);
    }
}
