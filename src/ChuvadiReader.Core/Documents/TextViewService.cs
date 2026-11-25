using System.Globalization;
using System.Text;

namespace ChuvadiReader.Core.Documents;

/// <summary>A text file loaded for viewing. <see cref="Text"/> is the decoded content exactly as
/// stored (only the byte-order mark is consumed by decoding); <see cref="Lines"/> is a display split
/// of it and never feeds back into the text.</summary>
public sealed record TextDocument(
    string Path,
    string Text,
    IReadOnlyList<string> Lines,
    string EncodingName,
    long SizeBytes)
{
    /// <summary>The code page the text was read as (see <see cref="TextEncodings"/>); zero when unknown.</summary>
    public int CodePage { get; init; }

    /// <summary>True when the encoding was chosen by hand rather than worked out from the file.</summary>
    public bool EncodingChosen { get; init; }

    /// <summary>How many bytes are not valid in the encoding the text was read as. For an automatic read this is
    /// zero unless the file fell back to Windows-1252 and holds bytes that page leaves undefined, a sign that it
    /// was written in some other encoding.</summary>
    public int BytesNotFitting { get; init; }
}

/// <summary>Loads Markdown, plain-text and HTML files for the view-only viewers (D-09): size limit,
/// encoding detection (BOM, then strict UTF-8, then Windows-1252) and line splitting. Pure BCL.</summary>
public sealed class TextViewService
{
    /// <summary>20 MB — beyond this the WebView becomes unusable for a rendered view.</summary>
    public const long DefaultMaxBytes = 20L * 1024 * 1024;

    // A NUL in the first few KB of a file without a BOM means it isn't text we can show.
    private const int BinaryProbeBytes = 8000;

    public TextViewService() : this(DefaultMaxBytes) { }

    public TextViewService(long maxBytes)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }

    public TextDocument Load(string path) => Load(path, codePage: null);

    /// <summary>Loads the file as the encoding with this code page (one of <see cref="TextEncodings.All"/>), or
    /// works the encoding out from the file when <paramref name="codePage"/> is null. A chosen encoding is never
    /// second-guessed: bytes that do not fit it are shown as U+FFFD and counted in
    /// <see cref="TextDocument.BytesNotFitting"/>.</summary>
    public TextDocument Load(string path, int? codePage)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DocumentViewException(ViewErrorReason.Other, "No file was given.");

        var info = new FileInfo(path);
        if (!info.Exists)
            throw new DocumentViewException(ViewErrorReason.Other, "The file couldn't be found.");
        if (info.Length > MaxBytes)
            throw TooLarge(info.Length);

        byte[] bytes;
        try
        {
            bytes = SharedRead.AllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentViewException(ViewErrorReason.Other, $"Couldn't read this file: {ex.Message}", ex);
        }

        if (bytes.LongLength > MaxBytes) throw TooLarge(bytes.LongLength); // grew since the stat

        if (codePage is int chosen)
        {
            var read = TextEncodings.Decode(bytes, chosen, out int notFitting);
            var name = TextEncodings.NameOf(chosen);
            return new TextDocument(path, read, SplitLines(read), name, bytes.LongLength)
            {
                CodePage = chosen,
                EncodingChosen = true,
                BytesNotFitting = notFitting,
            };
        }

        var text = Decode(bytes, out var encodingName);
        int detected = TextEncodings.CodePageOfDetected(encodingName);
        return new TextDocument(path, text, SplitLines(text), encodingName, bytes.LongLength)
        {
            CodePage = detected,
            BytesNotFitting = detected == TextEncodings.Windows1252 ? TextEncodings.UndefinedIn1252(bytes) : 0,
        };
    }

    private DocumentViewException TooLarge(long size) =>
        new(ViewErrorReason.Other,
            string.Format(CultureInfo.InvariantCulture,
                "This file is too large to view here ({0:0.0} MB; the limit is {1:0} MB).",
                size / (1024.0 * 1024.0), MaxBytes / (1024.0 * 1024.0)));

    /// <summary>Decodes file bytes: a BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) wins; otherwise
    /// strict UTF-8, falling back to Windows-1252. Throws <see cref="DocumentViewException"/>
    /// (<see cref="ViewErrorReason.BadFormat"/>) for BOM-less content that looks binary.</summary>
    private static string? LooksUtf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        int n = Math.Min(bytes.Length, BinaryProbeBytes) & ~1;
        if (n < 4 || (bytes.Length & 1) != 0) return null;
        int zeroHigh = 0, zeroLow = 0;
        for (int i = 0; i < n; i += 2)
        {
            byte hi = bigEndian ? bytes[i] : bytes[i + 1], lo = bigEndian ? bytes[i + 1] : bytes[i];
            if (hi == 0) zeroHigh++;
            if (lo == 0) zeroLow++;
        }

        int pairs = n / 2;
        if (zeroHigh * 10 < pairs * 4 || zeroLow * 20 > pairs) return null;   // most high bytes zero, almost no low
        string text;
        try
        {
            text = new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }

        int controls = 0;
        foreach (char c in text.AsSpan(0, Math.Min(text.Length, BinaryProbeBytes / 2)))
        {
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t' or '\f')) controls++;
        }

        return controls * 50 > Math.Min(text.Length, BinaryProbeBytes / 2) ? null : text;
    }

    public static string Decode(ReadOnlySpan<byte> bytes, out string encodingName)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            encodingName = "UTF-32 LE";
            return new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(bytes[4..]);
        }
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encodingName = "UTF-32 BE";
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(bytes[4..]);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encodingName = "UTF-8";
            return new UTF8Encoding(false).GetString(bytes[3..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encodingName = "UTF-16 LE";
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetString(bytes[2..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encodingName = "UTF-16 BE";
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetString(bytes[2..]);
        }

        // VW25: UTF-16 with no byte-order mark (Western text then has a zero in every other byte). It is told
        // from binary data by where the zeros fall and by decoding cleanly to text.
        foreach (bool be in new[] { true, false })
        {
            if (LooksUtf16(bytes, be) is { } text)
            {
                encodingName = be ? "UTF-16 BE" : "UTF-16 LE";
                return text;
            }
        }

        if (bytes[..Math.Min(bytes.Length, BinaryProbeBytes)].IndexOf((byte)0) >= 0)
            throw new DocumentViewException(ViewErrorReason.BadFormat,
                "This file looks like binary data, not text.");

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            encodingName = "UTF-8";
            return strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // T-01: a file that isn't Unicode is read as Windows-1252, the Western code page such files are written
            // in. It is Latin-1 with printable characters (curly quotes, dashes, €, ™) where Latin-1 has control codes.
            encodingName = "Windows-1252";
            return Windows1252(bytes);
        }
    }

    /// <summary>Windows-1252's characters for bytes 0x80 to 0x9F; every other byte is its Latin-1 character. The five
    /// bytes the code page leaves undefined (81, 8D, 8F, 90, 9D) stay the control codes they are, as Windows reads them.</summary>
    private const string Windows1252High =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F" +
        "\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    internal static string Windows1252(ReadOnlySpan<byte> bytes) =>
        string.Create(bytes.Length, bytes.ToArray(), static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                byte b = source[i];
                chars[i] = b is >= 0x80 and <= 0x9F ? Windows1252High[b - 0x80] : (char)b;
            }
        });

    /// <summary>Splits on CRLF, LF or CR. A final line break does not add an empty last line;
    /// empty text is one empty line.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r') continue;
            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length || lines.Count == 0) lines.Add(text[start..]);
        return lines;
    }
}
