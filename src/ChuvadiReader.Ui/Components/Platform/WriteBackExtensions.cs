using ChuvadiReader.Platform;

namespace ChuvadiReader.Ui.Components.Platform;

/// <summary>
/// One-line delivery of a file Chuvadi has just written (decision F-1 C). Call it inside the save's
/// existing try block, right after the write:
/// <code>await WriteBack.DeliverOrThrowAsync(path);</code>
/// Desktop hosts (Windows, macOS, Linux): returns at once — the path is the real file.
/// Android: writes the bytes back to the content URI. Web: hands the file to the browser as a download.
/// A failure throws <see cref="IOException"/> with a user-facing message, so the site's existing
/// catch shows it exactly like any other save error. A cancelled download is not an error.
/// </summary>
public static class WriteBackExtensions
{
    public static async Task DeliverOrThrowAsync(this IFileWriteBack writeBack, string? path, CancellationToken ct = default)
    {
        if (!writeBack.IsNeeded || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = await writeBack.CommitAsync(path, ct);
        if (result.Outcome == WriteBackOutcome.Failed)
        {
            throw new IOException(result.Message ?? "The file was written here but couldn't be delivered.");
        }
    }
}
