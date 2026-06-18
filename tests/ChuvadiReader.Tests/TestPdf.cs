using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Reader;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>Self-contained helpers: every fixture PDF is authored in code (no binary files),
/// pages are rasterised with the library's own renderer, and pixels are sampled straight out
/// of a BMP (uncompressed, trivial to parse) so the tests need no external tools and run the
/// same on Linux, macOS and Windows.</summary>
internal static class TestPdf
{
    /// <summary>Authors a blank white page of the given size (points) to a temp file and
    /// returns the path. Authored content is untagged (no StructTreeRoot).</summary>
    public static string BlankPage(double w = 200, double h = 200)
    {
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(w, h));
        pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
        return WriteTemp(b.ToByteArray());
    }

    /// <summary>Authors a white page with a single line of black text, returns the path.</summary>
    public static string TextPage(string text, double w = 300, double h = 120)
    {
        var b = PdfDocumentBuilder.Create();
        var pb = b.AddPage(new PageSize(w, h));
        pb.DrawRectangle(0, 0, w, h, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
        pb.DrawText(text, 20, 50, "Helvetica-Bold", 28, Color.FromHex("#000000"));
        return WriteTemp(b.ToByteArray());
    }

    public static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "chuvadi-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string TempOutput() =>
        Path.Combine(Path.GetTempPath(), "chuvadi-test-out-" + Guid.NewGuid().ToString("N") + ".pdf");

    /// <summary>A minimal solid-colour RGBA PNG (no external imaging libraries).</summary>
    public static byte[] SolidPng(int w, int h, byte r, byte g, byte b)
    {
        byte[] Chunk(string type, byte[] data)
        {
            using var ms = new MemoryStream();
            void Be(int v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
            Be(data.Length);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            var crcInput = new byte[t.Length + data.Length];
            Buffer.BlockCopy(t, 0, crcInput, 0, t.Length);
            Buffer.BlockCopy(data, 0, crcInput, t.Length, data.Length);
            ms.Write(t, 0, t.Length);
            ms.Write(data, 0, data.Length);
            Be((int)Crc32(crcInput));
            return ms.ToArray();
        }

        var ihdr = new byte[13];
        void PutBe(byte[] a, int o, int v) { a[o] = (byte)(v >> 24); a[o + 1] = (byte)(v >> 16); a[o + 2] = (byte)(v >> 8); a[o + 3] = (byte)v; }
        PutBe(ihdr, 0, w); PutBe(ihdr, 4, h);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0); // filter: none
            for (int x = 0; x < w; x++) { raw.WriteByte(r); raw.WriteByte(g); raw.WriteByte(b); raw.WriteByte(255); }
        }
        var idatData = ZlibCompress(raw.ToArray());

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        png.Write(Chunk("IHDR", ihdr));
        png.Write(Chunk("IDAT", idatData));
        png.Write(Chunk("IEND", Array.Empty<byte>()));
        return png.ToArray();
    }

    /// <summary>Renders a page to a BMP and samples the pixel at the given page fractions
    /// (0–1, top-left origin). Returns (R,G,B).</summary>
    public static (int R, int G, int B) SamplePixel(string pdfPath, int pageIndex, double fx, double fy, double dpi = 96)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var bmp = PdfRenderExtensions.RenderPageToBmp(doc, pageIndex, dpi);
        return SampleBmp(bmp, fx, fy);
    }

    /// <summary>Samples a 24/32-bit BMP at page fractions (top-left origin) → (R,G,B).</summary>
    public static (int R, int G, int B) SampleBmp(byte[] bmp, double fx, double fy)
    {
        int dataOffset = bmp[10] | (bmp[11] << 8) | (bmp[12] << 16) | (bmp[13] << 24);
        int width = bmp[18] | (bmp[19] << 8) | (bmp[20] << 16) | (bmp[21] << 24);
        int heightRaw = bmp[22] | (bmp[23] << 8) | (bmp[24] << 16) | (bmp[25] << 24);
        int bitCount = bmp[28] | (bmp[29] << 8);
        bool bottomUp = heightRaw > 0;
        int height = Math.Abs(heightRaw);
        int bytesPP = bitCount / 8;
        int rowSize = ((bitCount * width + 31) / 32) * 4;

        int px = Math.Clamp((int)(fx * width), 0, width - 1);
        int pyTop = Math.Clamp((int)(fy * height), 0, height - 1);
        int rowIndex = bottomUp ? (height - 1 - pyTop) : pyTop;

        int off = dataOffset + rowIndex * rowSize + px * bytesPP;
        // BMP stores BGR(A).
        int bch = bmp[off];
        int gch = bmp[off + 1];
        int rch = bmp[off + 2];
        return (rch, gch, bch);
    }

    public static bool IsWhitish((int R, int G, int B) p, int min = 230) => p.R >= min && p.G >= min && p.B >= min;
    public static bool IsReddish((int R, int G, int B) p) => p.R >= 150 && p.G <= 110 && p.B <= 110;
    public static bool IsBluish((int R, int G, int B) p) => p.B >= 150 && p.R <= 110 && p.G <= 130;
    public static bool IsGreenish((int R, int G, int B) p) => p.G >= 130 && p.R <= 130 && p.B <= 130;
    public static bool IsDark((int R, int G, int B) p, int max = 120) => p.R <= max && p.G <= max && p.B <= max;

    // ── tiny zlib + crc (no external deps) ───────────────────────────────────
    private static byte[] ZlibCompress(byte[] data)
    {
        using var outMs = new MemoryStream();
        outMs.WriteByte(0x78); outMs.WriteByte(0x9C); // zlib header
        using (var deflate = new System.IO.Compression.DeflateStream(outMs, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        uint a = Adler32(data);
        outMs.WriteByte((byte)(a >> 24)); outMs.WriteByte((byte)(a >> 16));
        outMs.WriteByte((byte)(a >> 8)); outMs.WriteByte((byte)a);
        return outMs.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data) { a = (a + d) % mod; b = (b + a) % mod; }
        return (b << 16) | a;
    }

    private static readonly uint[] _crcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var d in data) c = _crcTable[(c ^ d) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
