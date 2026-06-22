using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Windows.Platform;

/// <summary>Copies an image to the Windows clipboard on the UI thread.</summary>
public sealed class WpfImageClipboard : IImageClipboard
{
    public Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            return Task.CompletedTask;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.CompletedTask;
        }

        dispatcher.Invoke(() =>
        {
            try
            {
                var decoder = new PngBitmapDecoder(
                    new MemoryStream(pngBytes, writable: false),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                Clipboard.SetImage(frame);
            }
            catch
            {
                // The clipboard can be transiently locked by another app; ignore.
            }
        });

        return Task.CompletedTask;
    }
}
