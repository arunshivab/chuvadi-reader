namespace ChuvadiReader.Core.Reader;

/// <summary>Puts an image on the system clipboard, implemented per host.</summary>
public interface IImageClipboard
{
    /// <summary>Copy a PNG image (raw bytes) to the system clipboard.</summary>
    Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default);
}
