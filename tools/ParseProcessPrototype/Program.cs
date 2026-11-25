// TESTING ONLY (never shipped). D-198 B2 prototype for docs/foundation/PARSE-PROCESS-DESIGN.md.
//   engine                 the parse process: reads requests on stdin, answers on stdout (Wire)
//   bench <fixtures dir>   the app's side: measures, prints a report
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using ParseProcessPrototype;

if (args.Length > 0 && args[0] == "engine") return await Engine.RunAsync();
if (args.Length > 1 && args[0] == "bench") return await Bench.RunAsync(args[1], args.Skip(2).ToArray());
if (args.Length > 1 && args[0] == "makecad") return MakeCad.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 5, args.Length > 3 ? int.Parse(args[3]) : 30_000, args.Length > 4 ? int.Parse(args[4]) : 40);
if (args.Length > 1 && args[0] == "coldfirst")
{
    // The first document a running app opens today: library code loaded and compiled in the app's own process.
    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
    using var s = await new ChuvadiPdfReader().OpenAsync(args[1], Path.GetFileName(args[1]));
    var svg = await s.RenderPageSvgAsync(0);
    Console.WriteLine((System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds).ToString("0.0"));
    if (Environment.GetEnvironmentVariable("CHVD_SVG_OUT") is { Length: > 0 } o) File.WriteAllText(o, svg);
    return 0;
}
Console.Error.WriteLine("usage: engine | makecad <out.pdf> | bench <fixtures folder> [--heap-mb N] [--limit-s N] [--job-mb N] [--no-crash-watch]");
return 2;

static class Engine
{
    // The Word model has an abstract block type; the real contract would declare its kinds once (source-generated).
    internal static readonly JsonSerializerOptions Json = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                ti =>
                {
                    if (ti.Type != typeof(WordBlock)) return;
                    ti.PolymorphismOptions = new() { TypeDiscriminatorPropertyName = "k" };
                    foreach (var (t, k) in new (Type, string)[] { (typeof(WordHeading), "h"), (typeof(WordPara), "p"), (typeof(WordTableBlock), "t") })
                        ti.PolymorphismOptions.DerivedTypes.Add(new(t, k));
                },
            },
        },
    };

    public static async Task<int> RunAsync()
    {
        if (Environment.GetEnvironmentVariable("CHVD_ENGINE_QUIET") == "1") WindowsJob.QuietCrashes();
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        var reader = new ChuvadiPdfReader();
        IPdfSession? session = null;
        while (Wire.Read(input) is { } req)
        {
            string arg = Encoding.UTF8.GetString(req.Payload);
            try
            {
                switch (req.Code)
                {
                    case Wire.Ping: Wire.Write(output, Wire.Ok, Environment.ProcessId.ToString()); break;
                    case Wire.Open:
                        session?.Dispose();
                        session = await reader.OpenAsync(arg, Path.GetFileName(arg));
                        Wire.Write(output, Wire.Ok, session.Document.PageCount.ToString());
                        break;
                    case Wire.Render:
                        Wire.Write(output, Wire.Ok, await session!.RenderPageSvgAsync(int.Parse(arg)));
                        break;
                    case Wire.Close: session?.Dispose(); session = null; Wire.Write(output, Wire.Ok, ""); break;
                    case Wire.Word:
                        Wire.Write(output, Wire.Ok, JsonSerializer.SerializeToUtf8Bytes(new DocxViewService().Load(arg), Json));
                        break;
                    case Wire.Excel:
                        Wire.Write(output, Wire.Ok, JsonSerializer.SerializeToUtf8Bytes(new XlsxViewService().Load(arg), Json));
                        break;
                    case Wire.StackOverflow: Recurse(0); break;   // never returns: the process ends
                    case Wire.Spin: while (true) Thread.SpinWait(1000);
                    case Wire.Allocate:
                        var keep = new List<byte[]>();
                        while (true) { var b = new byte[16 << 20]; Array.Fill(b, (byte)1); keep.Add(b); }
                    case Wire.Stacks: Wire.Write(output, Wire.Ok, Stacks.Describe()); break;
                    case Wire.Echo: Wire.Write(output, Wire.Ok, req.Payload); break;
                    case Wire.Mem: Wire.Write(output, Wire.Ok, Engine.Memory()); break;
                    case Wire.Quit: session?.Dispose(); Wire.Write(output, Wire.Ok, ""); return 0;
                    default: Wire.Write(output, Wire.Failed, "unknown op"); break;
                }
            }
            catch (Exception ex)
            {
                Wire.Write(output, Wire.Failed, ex.GetType().Name + ": " + ex.Message);
            }
        }
        return 0;
    }

    /// <summary>What the engine holds now and at most: the GC's view (heap, committed, fragmented, the hard limit)
    /// and the process's (peak working set, private bytes). Reading it does a full collection first only for "heap
    /// now", so the peak numbers are what drawing really reached.</summary>
    internal static string Memory()
    {
        var me = Process.GetCurrentProcess();
        var g = GC.GetGCMemoryInfo(GCKind.Any);
        long mb(long b) => b >> 20;
        return $"GC heap {mb(g.HeapSizeBytes)} MB, committed {mb(g.TotalCommittedBytes)} MB, fragmented {mb(g.FragmentedBytes)} MB, " +
               $"GC limit {mb(g.TotalAvailableMemoryBytes)} MB, peak working set {mb(me.PeakWorkingSet64)} MB, private now {mb(me.PrivateMemorySize64)} MB, " +
               $"allocated in all {mb(GC.GetTotalAllocatedBytes())} MB";
    }

    static int Recurse(int n) { Span<byte> pad = stackalloc byte[256]; pad[0] = (byte)n; return Recurse(n + 1) + pad[0]; }
}

/// <summary>The app's side of one parse process: starts it, sends one request at a time, and treats the end of the
/// stream (the process died) or a missed time limit (it is then ended) as the answer "the engine is gone".</summary>
sealed class EngineClient : IDisposable
{
    public Process Process { get; }
    private readonly Stream _in, _out;
    public TimeSpan Limit { get; set; } = TimeSpan.FromSeconds(30);

    private EngineClient(Process p) { Process = p; _in = p.StandardInput.BaseStream; _out = p.StandardOutput.BaseStream; }

    /// <summary>Windows: put each engine in a job object with this memory cap (MB; 0 none).</summary>
    public static int JobMb;
    /// <summary>Quiet crashes in the engine (no Windows Error Reporting round), and end it as soon as the runtime
    /// says on stderr that it is going down ("Stack overflow", "Fatal error", "Process terminated").</summary>
    public static bool CrashWatch = true;
    private nint _job;
    private volatile string? _dying;
    private readonly ManualResetEventSlim _gone = new(false);

    public static EngineClient Start(int heapMb)
    {
        var self = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(self) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self) == "dotnet") psi.ArgumentList.Add(typeof(EngineClient).Assembly.Location);
        psi.ArgumentList.Add("engine");
        if (heapMb > 0) psi.Environment["DOTNET_GCHeapHardLimit"] = "0x" + ((long)heapMb << 20).ToString("X");
        psi.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        psi.Environment["DOTNET_EnableCrashReport"] = "0";
        psi.Environment["DOTNET_gcServer"] = "0";
        if (CrashWatch) psi.Environment["CHVD_ENGINE_QUIET"] = "1";
        var p = Process.Start(psi)!;
        var client = new EngineClient(p);
        if (JobMb > 0 || CrashWatch) client._job = WindowsJob.Contain(p.Handle, JobMb);
        // stderr is always read: an unread pipe can hold up a dying runtime that writes its whole stack there.
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { Length: > 0 } d) return;
            LastError = d;
            if (CrashWatch && client._dying is null &&
                (d.StartsWith("Stack overflow", StringComparison.Ordinal) || d.StartsWith("Fatal error", StringComparison.Ordinal) || d.StartsWith("Process terminated", StringComparison.Ordinal)))
            {
                client._dying = d;
                try { p.Kill(entireProcessTree: true); } catch { }
                client._gone.Set();
            }
        };
        p.BeginErrorReadLine();
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => client._gone.Set();
        return client;
    }

    public static string? LastError;

    /// <summary>Ok text, or throws EngineGone (died, or ended for passing the time limit) / EngineFailed.</summary>
    public byte[] Call(byte op, string arg = "")
    {
        Wire.Write(_in, op, arg);
        var read = Task.Run(() => Wire.Read(_out));
        int which = WaitHandle.WaitAny(new[] { ((IAsyncResult)read).AsyncWaitHandle, _gone.WaitHandle }, Limit);
        if (which == WaitHandle.WaitTimeout)
        {
            try { Process.Kill(entireProcessTree: true); } catch { }
            throw new EngineGone($"passed its time limit ({Limit.TotalSeconds:0} s) and was ended");
        }
        if (which == 1 && !read.IsCompleted)
        {
            Process.WaitForExit(2000);
            throw new EngineGone(_dying is { } why ? $"ended (the runtime said \"{why.Trim()}\"; ended at once)" : $"ended (exit code {(Process.HasExited ? Process.ExitCode : -1)})");
        }
        if (read.Result is not { } r)
        {
            // The pipe can close (and the read finish empty) before the gone signal wins the wait: when the crash
            // watch ended the engine, say so, rather than only the exit code its kill left (-1 on Windows).
            Process.WaitForExit(2000);
            throw new EngineGone(_dying is { } why ? $"ended (the runtime said \"{why.Trim()}\"; ended at once)" : $"ended (exit code {(Process.HasExited ? Process.ExitCode : -1)})");
        }
        if (r.Code != Wire.Ok) throw new EngineFailed(Encoding.UTF8.GetString(r.Payload));
        return r.Payload;
    }

    public string CallText(byte op, string arg = "") => Encoding.UTF8.GetString(Call(op, arg));

    /// <summary>The engine's own memory line, or why it could not be read (it may be gone).</summary>
    public string MemoryLine()
    {
        string job = OperatingSystem.IsWindows() && _job != 0 ? $"; job peak {WindowsJob.PeakMb(_job)} MB" : "";
        try { var l = Limit; Limit = TimeSpan.FromSeconds(10); try { return CallText(Wire.Mem) + job; } finally { Limit = l; } }
        catch (Exception ex) { return "not readable (" + ex.GetType().Name + ")" + job; }
    }

    public long RssKb()
    {
        try
        {
            if (OperatingSystem.IsLinux())
                foreach (var line in File.ReadLines($"/proc/{Process.Id}/status"))
                    if (line.StartsWith("VmRSS:")) return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            Process.Refresh();
            return Process.WorkingSet64 / 1024;
        }
        catch { return -1; }
    }

    public void Dispose()
    {
        try { if (!Process.HasExited) { Wire.Write(_in, Wire.Quit, ""); Process.WaitForExit(2000); } } catch { }
        try { if (!Process.HasExited) Process.Kill(); } catch { }
        Process.Dispose();
    }
}

sealed class EngineGone(string m) : Exception(m);
sealed class EngineFailed(string m) : Exception(m);
