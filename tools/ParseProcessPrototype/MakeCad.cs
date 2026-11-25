using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace ParseProcessPrototype;

/// <summary>TESTING ONLY. A made-up drawing that loads the renderer the way a large architectural CAD export does,
/// with nothing from any real building: five A1 sheets, each a grid of invented rooms with double walls, door
/// swings, hatching, dimension lines and labels (Type0 / Identity-H text through the library's LiPi fonts, Latin
/// and Tamil), about 30,000 short path segments a sheet (drawn as a picture, like the real
/// sheets: about as long to draw as a real A1 CAD sheet on the same machine). Seeded, so every run writes the same file.</summary>
static class MakeCad
{
    public static int Run(string outPath, int sheets = 5, int segmentsPerSheet = 30_000, int perPath = 40)
    {
        var rnd = new Random(20261124);
        var doc = PdfDocumentBuilder.Create();
        doc.SetTitle("Synthetic CAD-like drawing (made up, for testing)");
        doc.SetAuthor("ParseProcessPrototype makecad");
        doc.UseLipiFonts();
        var ink = Color.FromHex("#202020");
        var hatch = Color.FromHex("#707070");
        var dim = Color.FromHex("#1050A0");
        const double W = 1684, H = 2384, M = 60;   // A1 portrait, in points
        long total = 0;
        for (int s = 0; s < sheets; s++)
        {
            var pb = doc.AddPage(new PageSize(W, H));
            int segs = 0;
            var walls = new Path(); var hatches = new Path(); var dims = new Path();
            void Flush()
            {
                pb.DrawPath(walls, ink, null, 0.6); pb.DrawPath(hatches, hatch, null, 0.2); pb.DrawPath(dims, dim, null, 0.25);
                walls = new Path(); hatches = new Path(); dims = new Path();
            }
            pb.DrawRectangle(M / 2, M / 2, W - M, H - M, ink, null, 1.5);
            pb.DrawText($"SHEET {s + 1} OF {sheets}  ·  MADE-UP PLAN FOR TESTING  ·  தாள் {s + 1}", M, H - M / 2 - 20, "Lipi", 14, ink);
            int room = 0;
            while (segs < segmentsPerSheet)
            {
                double rw = 24 + rnd.NextDouble() * 56, rh = 24 + rnd.NextDouble() * 56;   // small rooms: short strokes, as in a real sheet
                double x = M + rnd.NextDouble() * (W - 2 * M - rw), y = M + 40 + rnd.NextDouble() * (H - 2 * M - 80 - rh);
                walls.Rectangle(x, y, rw, rh); walls.Rectangle(x + 4, y + 4, rw - 8, rh - 8); segs += 8;
                // a door swing on one wall
                double dx = x + 6 + rnd.NextDouble() * (rw - 18);
                walls.MoveTo(dx, y); walls.LineTo(dx, y + 10);
                walls.CubicBezierTo(dx + 5.5, y + 10, dx + 10, y + 5.5, dx + 10, y); segs += 2;
                // hatching in about a third of the rooms
                if (rnd.Next(3) == 0)
                    for (double t = 6; t < rw + rh - 12; t += 3)
                    {
                        double x0 = x + 4 + Math.Min(t, rw - 8), y0 = y + 4 + Math.Max(0, t - (rw - 8));
                        double x1 = x + 4 + Math.Max(0, t - (rh - 8)), y1 = y + 4 + Math.Min(t, rh - 8);
                        hatches.MoveTo(x0, y0); hatches.LineTo(x1, y1); segs++;
                    }
                // a dimension line under the room, with ticks
                dims.MoveTo(x, y - 12); dims.LineTo(x + rw, y - 12);
                dims.MoveTo(x, y - 16); dims.LineTo(x, y - 8); dims.MoveTo(x + rw, y - 16); dims.LineTo(x + rw, y - 8); segs += 3;
                room++;
                if (room % 6 == 0)
                    pb.DrawText(room % 12 == 0 ? $"அறை {room}  {rw * 10:0}x{rh * 10:0}" : $"ROOM {s + 1}.{room}  {rw * 10:0}x{rh * 10:0}",
                        x + 6, y + rh / 2, "Lipi", 4, ink);
                if (segs / perPath != (segs - 14) / perPath) Flush();
            }
            Flush();
            total += segs;
        }
        var built = doc.ToByteArray();
        var withIndirectW = MoveWidthsIntoOwnObjects(built);
        // Flate the content streams, as a CAD export would be: the drawing then weighs megabytes, not tens of them.
        using (var src = PdfDocument.Open(new MemoryStream(withIndirectW, writable: false)))
        using (var f = File.Create(outPath))
            PdfCompressor.Compress(src, f, (CompressionOptions?)null);
        Console.WriteLine($"wrote {outPath}: {sheets} sheets, about {total:N0} path segments, {new FileInfo(outPath).Length:N0} bytes");
        return 0;
    }

    /// <summary>Each CID font's /W array moved into an object of its own (12 0 R) by an incremental update, the way
    /// many CAD exporters write it, so a reader must resolve /W through a reference.</summary>
    static byte[] MoveWidthsIntoOwnObjects(byte[] pdf)
    {
        using var d = PdfDocument.Open(new MemoryStream(pdf, writable: false));
        var store = d.Objects;
        int next = d.Trailer.GetInteger("Size", 0);
        var changed = new List<PdfIndirectObject>(); var seen = new HashSet<int>();
        for (int i = 0; i < d.PageCount; i++)
        {
            var res = store.ResolveDictionaryEntry<PdfDictionary>(d.Pages[i].Dictionary, "Resources");
            var fonts = res is null ? null : store.ResolveDictionaryEntry<PdfDictionary>(res, "Font");
            if (fonts is null) continue;
            foreach (var key in fonts.Keys)
            {
                var font = store.ResolveAs<PdfDictionary>(fonts[key]);
                if (font?.GetName("Subtype")?.ToString() is not ("Type0" or "/Type0")) continue;
                var desc = store.ResolveDictionaryEntry<PdfArray>(font, "DescendantFonts");
                if (desc is null || desc.Count == 0 || desc[0] is not PdfReference r || !seen.Add(r.ObjectNumber)) continue;
                var cid = store.ResolveAs<PdfDictionary>(r);
                if (cid?.GetArray("W") is not { } w) continue;
                var wId = new PdfObjectId(next++, 0);
                changed.Add(new PdfIndirectObject(wId, w));
                cid.Set("W", new PdfReference(wId));
                changed.Add(new PdfIndirectObject(r.ObjectId, cid));
            }
        }
        if (changed.Count == 0) throw new InvalidOperationException("no CID font widths found to move");
        var trailer = new PdfDictionary();
        trailer.Set("Size", next);
        trailer.Set("Root", d.Trailer["Root"]);
        return PdfWriter.WriteIncrementalUpdate(pdf, changed, trailer);
    }
}
