namespace ChuvadiReader.Core.Reader;

/// <summary>
/// CR70 / CR71: an image put on a desk is checked first, from its header and its end, without decoding it: it must
/// be a PNG, JPEG, GIF, BMP or WebP Chuvadi can read, whole (not cut short), and of a size a page can hold. A damaged
/// or enormous image was taken before and Bind then failed with the decoder's own text.
/// </summary>
public static class ImageCheck
{
    /// <summary>The largest image a desk takes: 100 million pixels, and 30,000 px a side.</summary>
    public const long MaxPixels = 100_000_000;

    public const int MaxSide = 30_000;

    /// <summary>Null when <paramref name="path"/> can go on a desk; otherwise the sentence saying why not.</summary>
    public static string? Refusal(string path)
    {
        var name = Path.GetFileName(path);
        byte[] head, tail;
        long length;
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            length = f.Length;
            head = new byte[(int)Math.Min(length, 64 * 1024)];
            f.ReadExactly(head);
            tail = new byte[(int)Math.Min(length, 16)];
            f.Seek(-tail.Length, SeekOrigin.End);
            f.ReadExactly(tail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{name} couldn't be read.";
        }

        var (kind, w, h, whole) = Inspect(head, tail);
        if (kind is null) return $"{name} isn't an image Chuvadi can put on a page (PNG, JPEG, GIF, BMP or WebP).";
        if (!whole) return $"{name} is cut short or damaged, so it wasn't put on the desk.";
        if (w <= 0 || h <= 0) return $"{name} is damaged (its size can't be read), so it wasn't put on the desk.";
        if (w > MaxSide || h > MaxSide || (long)w * h > MaxPixels)
            return $"{name} is too large to put on a page ({w:N0} × {h:N0} pixels). Chuvadi takes images up to {MaxSide:N0} pixels a side.";
        return null;
    }

    internal static (string? Kind, int W, int H, bool Whole) Inspect(ReadOnlySpan<byte> b, ReadOnlySpan<byte> tail)
    {
        static int Be32(ReadOnlySpan<byte> s, int i) => (s[i] << 24) | (s[i + 1] << 16) | (s[i + 2] << 8) | s[i + 3];
        static int Be16(ReadOnlySpan<byte> s, int i) => (s[i] << 8) | s[i + 1];
        static int Le16(ReadOnlySpan<byte> s, int i) => s[i] | (s[i + 1] << 8);
        static int Le32(ReadOnlySpan<byte> s, int i) => s[i] | (s[i + 1] << 8) | (s[i + 2] << 16) | (s[i + 3] << 24);

        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ("png", Be32(b, 16), Be32(b, 20), tail.IndexOf("IEND"u8) >= 0);
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                byte m = b[i + 1];
                if (m is >= 0xC0 and <= 0xCF && m is not (0xC4 or 0xC8 or 0xCC))
                    return ("jpeg", Be16(b, i + 7), Be16(b, i + 5), tail.Length >= 2 && tail[^2] == 0xFF && tail[^1] == 0xD9);
                if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                i += 2 + Be16(b, i + 2);
            }

            return ("jpeg", 0, 0, true);
        }
        if (b.Length >= 10 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8)))
            return ("gif", Le16(b, 6), Le16(b, 8), tail.Length > 0 && tail[^1] == 0x3B);
        if (b.Length >= 26 && b[0] == (byte)'B' && b[1] == (byte)'M')
            return ("bmp", Le32(b, 18), Math.Abs(Le32(b, 22)), true);
        if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
        {
            if (b[12..16].SequenceEqual("VP8X"u8)) return ("webp", 1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16)), true);
            if (b[12..16].SequenceEqual("VP8L"u8)) { int v = Le32(b, 21); return ("webp", 1 + (v & 0x3FFF), 1 + ((v >> 14) & 0x3FFF), true); }
            if (b[12..16].SequenceEqual("VP8 "u8)) return ("webp", Le16(b, 26) & 0x3FFF, Le16(b, 28) & 0x3FFF, true);
        }

        return (null, 0, 0, false);
    }
}
