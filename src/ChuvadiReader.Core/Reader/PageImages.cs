using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// D-158 part 2 (twenty-first patch): drawn page images, kept in memory and served to the Ui by address, so a heavy
/// page no longer travels as a ~77 MB base64 string through the render tree and the transport. Each image has an
/// unguessable token (128 random bits) and an owner. It's served only to that owner and freed when its page is
/// dropped or its document closes. In the app hosts, the WebView reads it through their file provider (in
/// process: no localhost server, nothing leaves the device). On the portal, a same-origin endpoint tied to the
/// signed-in user's session reads it. One store per process.
/// </summary>
public sealed class PageImages
{
    /// <summary>Where page images live, relative to the Ui's base address: <c>_chvd/pages/&lt;token&gt;.png</c>.</summary>
    public const string PathPrefix = "_chvd/pages/";

    private const string Extension = ".png";

    private readonly ConcurrentDictionary<string, Entry> _images = new(StringComparer.Ordinal);

    private sealed record Entry(string Owner, byte[] Png);

    /// <summary>Bytes held right now (all owners).</summary>
    public long HeldBytes => _images.Values.Sum(e => (long)e.Png.Length);

    /// <summary>Images held right now (all owners).</summary>
    public int Count => _images.Count;

    /// <summary>The Ui address of an image, relative to the base address.</summary>
    public static string AddressOf(string token) => PathPrefix + token + Extension;

    /// <summary>Keeps <paramref name="png"/> for <paramref name="owner"/> and returns its new token.</summary>
    public string Add(string owner, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(png);
        while (true)
        {
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            if (_images.TryAdd(token, new Entry(owner, png)))
            {
                return token;
            }
        }
    }

    /// <summary>The image for <paramref name="token"/>, if it exists and belongs to <paramref name="owner"/>.
    /// Anyone else, or a freed token, gets nothing, the same as a token that never existed.</summary>
    public bool TryGet(string token, string owner, out ReadOnlyMemory<byte> png)
    {
        png = default;
        if (token is null || owner is null || !_images.TryGetValue(token, out var entry)
            || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(entry.Owner), System.Text.Encoding.UTF8.GetBytes(owner)))
        {
            return false;
        }

        png = entry.Png;
        return true;
    }

    /// <summary>Reads the token from a request path or file name (<c>_chvd/pages/&lt;token&gt;.png</c>,
    /// <c>/_chvd/pages/…</c> or just <c>&lt;token&gt;.png</c>). False for anything else.</summary>
    public static bool TryParseAddress(string? path, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var p = path.TrimStart('/');
        if (p.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            p = p[PathPrefix.Length..];
        }

        if (!p.EndsWith(Extension, StringComparison.Ordinal) || p.Length != 32 + Extension.Length)
        {
            return false;
        }

        var t = p[..32];
        foreach (var c in t)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        token = t;
        return true;
    }

    /// <summary>Frees one image. Its address answers nothing from now on.</summary>
    public void Remove(string token) => _images.TryRemove(token, out _);

    /// <summary>WT-21: frees every image <paramref name="owner"/> holds — a portal session that ended (expiry or
    /// sign-out) keeps no page pixels in its worker. Returns how many were freed. Other owners' images stay.</summary>
    public int RemoveOwner(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var freed = 0;
        foreach (var pair in _images)
        {
            if (string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal) && _images.TryRemove(pair))
            {
                freed++;
            }
        }

        return freed;
    }
}
