using System.Windows;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Windows.Platform;

/// <summary>
/// Text and image clipboard for the Windows host (IClipboard). Text goes through the WPF clipboard
/// on the UI thread; images reuse <see cref="WpfImageClipboard"/> unchanged.
/// A failed text copy throws, so the Ui keeps showing "Couldn't copy: …" as it does today.
/// </summary>
public sealed class WpfClipboard : IClipboard
{
    private readonly WpfImageClipboard _images = new();

    public bool CanCopyText => true;

    public bool CanCopyImage => true;

    public Task SetTextAsync(string text, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("The clipboard isn't available right now.");

        // Another app can hold the clipboard open for a moment (CLIPBRD_E_CANT_OPEN): retry briefly,
        // then let the last failure surface.
        dispatcher.Invoke(() =>
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text ?? string.Empty, copy: true);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException) when (attempt < 5)
                {
                    Thread.Sleep(50);
                }
            }
        });
        return Task.CompletedTask;
    }

    public Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default) => _images.SetImageAsync(pngBytes, ct);
}
