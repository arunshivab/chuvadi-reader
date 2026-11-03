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
    long SizeBytes);

/// <summary>Loads Markdown, plain-text and HTML files for the view-only viewers (D-09): size limit,
/// encoding detection (BOM, then strict UTF-8, then Latin-1) and line splitting. Pure BCL.</summary>
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

    public TextDocument Load(string path)
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
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentViewException(ViewErrorReason.Other, $"Couldn't read this file: {ex.Message}", ex);
        }

        if (bytes.LongLength > MaxBytes) throw TooLarge(bytes.LongLength); // grew since the stat

        var text = Decode(bytes, out var encodingName);
        return new TextDocument(path, text, SplitLines(text), encodingName, bytes.LongLength);
    }

    private DocumentViewException TooLarge(long size) =>
        new(ViewErrorReason.Other,
            string.Format(CultureInfo.InvariantCulture,
                "This file is too large to view here ({0:0.0} MB; the limit is {1:0} MB).",
                size / (1024.0 * 1024.0), MaxBytes / (1024.0 * 1024.0)));

    /// <summary>Decodes file bytes: a BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) wins; otherwise
    /// strict UTF-8, falling back to Latin-1. Throws <see cref="DocumentViewException"/>
    /// (<see cref="ViewErrorReason.BadFormat"/>) for BOM-less content that looks binary.</summary>
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
            encodingName = "Latin-1";
            return Encoding.Latin1.GetString(bytes);
        }
    }

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
