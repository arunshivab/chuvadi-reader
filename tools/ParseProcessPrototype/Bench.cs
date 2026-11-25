using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using ParseProcessPrototype;

static class Bench
{
    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    static double Median(List<double> v) { if (v.Count == 0) return double.NaN; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    static string F(double v) => v.ToString("0.0");

    public static async Task<int> RunAsync(string folder, string[] opts)
    {
        int heapMb = Opt(opts, "--heap-mb", 512);
        int limitS = Opt(opts, "--limit-s", 10);
        int pagesPerFile = Opt(opts, "--pages", 8);
        EngineClient.JobMb = Opt(opts, "--job-mb", 0);                 // Windows: the job object's hard cap
        EngineClient.CrashWatch = !opts.Contains("--no-crash-watch");   // to compare against the plain wait
        // The heaviest file first: it is the one the "first document" line and the warm-up use.
        var pdfs = Directory.GetFiles(folder, "*.pdf").Where(p => !Path.GetFileName(p).StartsWith("bomb")).OrderByDescending(p => new FileInfo(p).Length).ThenBy(p => p).ToList();
        var bombs = Directory.GetFiles(folder, "bomb*.pdf").ToList();
        var docx = Directory.GetFiles(folder, "*.docx").OrderBy(p => p).ToList();
        var xlsx = Directory.GetFiles(folder, "*.xlsx").OrderBy(p => p).ToList();

        Console.WriteLine($"# Parse-process prototype — {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores, .NET {Environment.Version}");
        Console.WriteLine($"engine: heap cap {heapMb} MB (DOTNET_GCHeapHardLimit), time limit {limitS} s per request" +
            (OperatingSystem.IsWindows() ? $", job object {(EngineClient.JobMb > 0 ? EngineClient.JobMb + " MB cap" : "no memory cap")}" : "") +
            $", crash watch {(EngineClient.CrashWatch ? "on" : "off")}");
        Console.WriteLine();
        Console.WriteLine($"## Stack sizes\napp process: {Stacks.Describe()}");

        // ── 1. Starting the engine ──
        var starts = new List<double>(); long idleRss = 0;
        for (int i = 0; i < 8; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            using var e = EngineClient.Start(heapMb);
            e.CallText(Wire.Ping);
            starts.Add(Ms(Stopwatch.GetTimestamp() - t0));
            if (i == 7) { await Task.Delay(300); idleRss = e.RssKb(); Console.WriteLine($"engine process: {e.CallText(Wire.Stacks)}"); }
        }
        Console.WriteLine();
        Console.WriteLine("## 1. Starting the engine (process start to first answer)");
        Console.WriteLine($"first {F(starts[0])} ms; then median {F(Median(starts.Skip(1).ToList()))} ms (min {F(starts.Skip(1).Min())}, max {F(starts.Skip(1).Max())}); engine memory idle {idleRss / 1024} MB");

        // ── 1a. The first document an app opens: today against with an engine started for it ──
        if (pdfs.FirstOrDefault() is { } first)
        {
            var today = new List<double>(); var withEngine = new List<double>();
            for (int i = 0; i < 6; i++)
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, UseShellExecute = false };
                if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") psi.ArgumentList.Add(typeof(Bench).Assembly.Location);
                psi.ArgumentList.Add("coldfirst"); psi.ArgumentList.Add(first);
                using (var p = Process.Start(psi)!) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); today.Add(double.Parse(o.Trim(), System.Globalization.CultureInfo.InvariantCulture)); }
                long t0 = Stopwatch.GetTimestamp();
                using var e = EngineClient.Start(heapMb); e.CallText(Wire.Open, first); e.CallText(Wire.Render, "0");
                withEngine.Add(Ms(Stopwatch.GetTimestamp() - t0));
            }
            Console.WriteLine($"first document, open and page 1 ({Path.GetFileName(first)}): today, in a running app {F(Median(today))} ms; with an engine started for it (start included) {F(Median(withEngine))} ms; with an engine already running, see table 2");
        }

        // ── 1b. The wire itself: a payload there and back ──
        using (var e = EngineClient.Start(heapMb))
        {
            var line = new List<string>();
            foreach (int kb in new[] { 1, 100, 1000, 6000 })
            {
                var text = new string('x', kb * 1024); var t = new List<double>();
                for (int i = 0; i < 12; i++) { long t0 = Stopwatch.GetTimestamp(); e.Call(Wire.Echo, text); t.Add(Ms(Stopwatch.GetTimestamp() - t0)); }
                line.Add($"{kb} KB {F(Median(t.Skip(2).ToList()))} ms");
            }
            Console.WriteLine($"wire, a payload sent and returned (twice the one-way cost): {string.Join(", ", line)}");
        }

        // ── 2. Drawing PDF pages: in-process against the engine ──
        Console.WriteLine();
        Console.WriteLine($"## 2. PDF pages (open, then the first {pagesPerFile} pages), warm: one file drawn first on both sides");
        Console.WriteLine("| file | pages | SVG KB/page | open in-process | open engine | page in-process (median) | page engine (median) | overhead/page |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        var reader = new ChuvadiPdfReader();
        // Every file is tried on its own: an engine that fails or dies is reported on that file's row with what it
        // held, and a new engine carries on with the next file, so one bad result never ends the run.
        var pdfEngine = EngineClient.Start(heapMb);
        pdfEngine.Limit = TimeSpan.FromSeconds(limitS * 6);
        var memLines = new List<string>();
        try
        {
            if (pdfs.Count > 0)
            {
                await DrawInProcess(reader, pdfs[0], pagesPerFile);
                try { DrawEngine(pdfEngine, pdfs[0], pagesPerFile); }
                catch (Exception ex) when (ex is EngineFailed or EngineGone)
                {
                    memLines.Add($"- warm-up on {Path.GetFileName(pdfs[0])} failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}; engine then: {pdfEngine.MemoryLine()}");
                    pdfEngine.Dispose(); pdfEngine = EngineClient.Start(heapMb); pdfEngine.Limit = TimeSpan.FromSeconds(limitS * 6);
                }
            }
            foreach (var pdf in pdfs)
            {
                Drawn a = null!, b = null!; var ae = new List<double>(); var be = new List<double>();
                string? failed = null;
                for (int round = 0; round < 3 && failed is null; round++)
                {
                    a = await DrawInProcess(reader, pdf, pagesPerFile); ae.AddRange(a.Each);
                    try { b = DrawEngine(pdfEngine, pdf, pagesPerFile); be.AddRange(b.Each); }
                    catch (Exception ex) when (ex is EngineFailed or EngineGone) { failed = $"round {round + 1}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
                }
                a = a with { Each = ae };
                if (failed is null)
                {
                    b = b with { Each = be };
                    Console.WriteLine($"| {Path.GetFileName(pdf)} | {a.Pages} | {a.SvgKb / Math.Max(1, a.Count):0} | {F(a.Open)} ms | {F(b.Open)} ms | {F(Median(a.Each))} ms | {F(Median(b.Each))} ms | {F(Median(b.Each) - Median(a.Each))} ms |");
                    memLines.Add($"- after {Path.GetFileName(pdf)}: {pdfEngine.MemoryLine()}");
                }
                else
                {
                    Console.WriteLine($"| {Path.GetFileName(pdf)} | {a.Pages} | {a.SvgKb / Math.Max(1, a.Count):0} | {F(a.Open)} ms | engine failed | {F(Median(a.Each))} ms | engine failed | |");
                    memLines.Add($"- {Path.GetFileName(pdf)}: engine failed on {failed}; engine then: {pdfEngine.MemoryLine()}");
                    pdfEngine.Dispose(); pdfEngine = EngineClient.Start(heapMb); pdfEngine.Limit = TimeSpan.FromSeconds(limitS * 6);
                }
            }
            Console.WriteLine($"engine memory after drawing: {pdfEngine.RssKb() / 1024} MB");
            Console.WriteLine();
            Console.WriteLine("Engine memory per file (GC and process; on Windows the job's peak, which is what the cap counts):");
            foreach (var l in memLines) Console.WriteLine(l);
        }
        finally { pdfEngine.Dispose(); }

        // ── 3. Word and Excel: the model crosses as JSON ──
        Console.WriteLine();
        Console.WriteLine("## 3. Word and Excel (load in-process, against load in the engine plus the model sent back as JSON)");
        Console.WriteLine("| file | in-process | engine | JSON KB |");
        Console.WriteLine("|---|---|---|---|");
        using (var engine = EngineClient.Start(heapMb))
        {
            engine.Limit = TimeSpan.FromSeconds(limitS * 6);
            foreach (var (files, op) in new[] { (docx, Wire.Word), (xlsx, Wire.Excel) })
                foreach (var f in files)
                {
                    try
                    {
                        Load(f, op); engine.Call(op, f);   // warm
                        var ins = new List<double>(); var eng = new List<double>(); int kb = 0;
                        for (int i = 0; i < 5; i++)
                        {
                            long t0 = Stopwatch.GetTimestamp(); Load(f, op); ins.Add(Ms(Stopwatch.GetTimestamp() - t0));
                            t0 = Stopwatch.GetTimestamp(); var bytes = engine.Call(op, f); eng.Add(Ms(Stopwatch.GetTimestamp() - t0)); kb = bytes.Length / 1024;
                        }
                        Console.WriteLine($"| {Path.GetFileName(f)} | {F(Median(ins))} ms | {F(Median(eng))} ms | {kb} |");
                    }
                    catch (Exception ex) { Console.WriteLine($"| {Path.GetFileName(f)} | failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]} | | |"); }
                }
        }

        // ── 4. When the engine dies or runs away, the app does not ──
        Console.WriteLine();
        Console.WriteLine("## 4. Failure: the engine dies or runs away; the app carries on");
        var sample = pdfs.FirstOrDefault();
        foreach (var (name, op) in new[] { ("stack overflow (uncatchable)", Wire.StackOverflow), ("endless loop", Wire.Spin), ("allocates without end", Wire.Allocate) })
        {
            using var e = EngineClient.Start(heapMb);
            e.Limit = TimeSpan.FromSeconds(limitS);
            try { if (sample is not null) { e.CallText(Wire.Open, sample); e.CallText(Wire.Render, "0"); } }
            catch (Exception ex) when (ex is EngineFailed or EngineGone) { Console.WriteLine($"- {name}: the sample file failed first ({ex.Message.Split('\n')[0]}); {e.MemoryLine()}"); continue; }
            long t0 = Stopwatch.GetTimestamp(); string outcome;
            try { e.Call(op); outcome = "answered (unexpected)"; }
            catch (EngineGone g) { outcome = "engine " + g.Message; }
            catch (EngineFailed f) { outcome = "engine refused and stayed up: " + f.Message; }
            double seen = Ms(Stopwatch.GetTimestamp() - t0);
            long t1 = Stopwatch.GetTimestamp(); string back = "";
            if (sample is not null)
            {
                using var again = EngineClient.Start(heapMb);
                try
                {
                    again.CallText(Wire.Open, sample); var svg = again.CallText(Wire.Render, "0");
                    back = $"; new engine, file reopened and page 1 drawn again in {F(Ms(Stopwatch.GetTimestamp() - t1))} ms ({svg.Length / 1024} KB)";
                }
                catch (Exception ex) when (ex is EngineFailed or EngineGone) { back = $"; new engine could not draw page 1 again: {ex.Message.Split('\n')[0]}"; }
            }
            Console.WriteLine($"- {name}: {outcome}, noticed after {F(seen)} ms{back}. App still running: yes.");
        }
        // ── 5. The library chats' probe files (folder "probes"): each opened in a fresh engine ──
        var probeDir = Path.Combine(folder, "probes");
        if (Directory.Exists(probeDir))
        {
            Console.WriteLine();
            Console.WriteLine("## 5. Probe files (each in its own engine; the app must stay up)");
            foreach (var f in Directory.GetFiles(probeDir).OrderBy(x => x))
            {
                using var e = EngineClient.Start(heapMb);
                e.Limit = TimeSpan.FromSeconds(limitS);
                long t0 = Stopwatch.GetTimestamp(); string outcome;
                try
                {
                    switch (Path.GetExtension(f).ToLowerInvariant())
                    {
                        case ".pdf":
                            int n = int.Parse(e.CallText(Wire.Open, f));
                            for (int i = 0; i < n; i++) e.Call(Wire.Render, i.ToString());
                            outcome = $"drew all {n} pages"; break;
                        case ".docx": e.Call(Wire.Word, f); outcome = "loaded"; break;
                        case ".xlsx": e.Call(Wire.Excel, f); outcome = "loaded"; break;
                        default: outcome = "skipped (not a PDF, Word or Excel file)"; break;
                    }
                }
                catch (EngineGone g) { outcome = "engine " + g.Message; }
                catch (EngineFailed x) { outcome = "refused: " + x.Message.Split('\n')[0]; }
                catch (Exception x) { outcome = "bench error: " + x.GetType().Name + ": " + x.Message.Split('\n')[0]; }
                Console.WriteLine($"- {Path.GetFileName(f)}: {outcome} after {F(Ms(Stopwatch.GetTimestamp() - t0))} ms. App still running: yes.");
            }
        }

        foreach (var bomb in bombs)
        {
            using var e = EngineClient.Start(heapMb);
            e.Limit = TimeSpan.FromSeconds(limitS);
            long t0 = Stopwatch.GetTimestamp(); string outcome;
            try { e.CallText(Wire.Open, bomb); e.Call(Wire.Render, "0"); outcome = "drew it"; }
            catch (EngineGone g) { outcome = "engine " + g.Message; }
            catch (EngineFailed f) { outcome = "refused: " + f.Message; }
            Console.WriteLine($"- {Path.GetFileName(bomb)}: {outcome} after {F(Ms(Stopwatch.GetTimestamp() - t0))} ms. App still running: yes.");
        }
        return 0;
    }

    static object Load(string f, byte op) => op == Wire.Word ? new DocxViewService().Load(f) : new XlsxViewService().Load(f);

    record Drawn(int Pages, int Count, long SvgKb, double Open, List<double> Each);

    static async Task<Drawn> DrawInProcess(ChuvadiPdfReader reader, string pdf, int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        using var s = await reader.OpenAsync(pdf, Path.GetFileName(pdf));
        double open = Ms(Stopwatch.GetTimestamp() - t0);
        int pages = s.Document.PageCount, count = Math.Min(n, pages); long kb = 0; var each = new List<double>();
        for (int i = 0; i < count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            var svg = await s.RenderPageSvgAsync(i);
            each.Add(Ms(Stopwatch.GetTimestamp() - t)); kb += svg.Length / 1024;
        }
        return new(pages, count, kb, open, each);
    }

    static Drawn DrawEngine(EngineClient e, string pdf, int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        int pages = int.Parse(e.CallText(Wire.Open, pdf));
        double open = Ms(Stopwatch.GetTimestamp() - t0);
        int count = Math.Min(n, pages); long kb = 0; var each = new List<double>();
        for (int i = 0; i < count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            var svg = e.CallText(Wire.Render, i.ToString());
            each.Add(Ms(Stopwatch.GetTimestamp() - t)); kb += svg.Length / 1024;
        }
        e.CallText(Wire.Close);
        return new(pages, count, kb, open, each);
    }

    static int Opt(string[] o, string name, int dflt) { int i = Array.IndexOf(o, name); return i >= 0 && i + 1 < o.Length && int.TryParse(o[i + 1], out var v) ? v : dflt; }
}
