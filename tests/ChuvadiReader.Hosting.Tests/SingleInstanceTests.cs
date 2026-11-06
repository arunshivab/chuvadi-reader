using ChuvadiReader.Core.Lifecycle;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Hosting;
using ChuvadiReader.Hosting.Desktop;
using ChuvadiReader.Platform;
using Xunit;

namespace ChuvadiReader.Hosting.Tests;

/// <summary>Single instance (W-A / L-A / F-1). Runs the real mutex/pipe path on Windows runners and the
/// real lock-file/socket path on Linux and macOS runners, within one process.</summary>
public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _app = "Chuvadi.Test." + Guid.NewGuid().ToString("N")[..8];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chvd-si-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = new();

    public SingleInstanceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private DesktopSingleInstance Claim(params string[] args)
    {
        var i = DesktopSingleInstance.Claim(args, _app);
        _owned.Add(i);
        return i;
    }

    private string Pdf(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "%PDF-1.7");
        return p;
    }

    private static bool RunNow(Action a, TimeSpan _) { a(); return true; }

    [Fact]
    public async Task Second_launch_hands_its_files_to_the_running_copy()
    {
        var primary = Claim();
        Assert.Equal(InstanceRole.Primary, primary.Role);
        var got = new TaskCompletionSource<IReadOnlyList<string>>();
        primary.FilesReceived += f => got.TrySetResult(f);
        primary.StartListening(RunNow);

        var a = Pdf("a.pdf");
        var b = Pdf("b.pdf");
        var second = Claim(a, b);

        Assert.Equal(InstanceRole.HandedOver, second.Role);
        Assert.Equal([a, b], await got.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_launch_while_the_running_copy_is_still_starting_waits_for_it()
    {
        var primary = Claim();
        var got = new TaskCompletionSource<IReadOnlyList<string>>();
        primary.FilesReceived += f => got.TrySetResult(f);
        var a = Pdf("a.pdf");

        var second = Task.Run(() => Claim(a));
        await Task.Delay(500); // primary "paints" late
        primary.StartListening(RunNow);

        Assert.Equal(InstanceRole.HandedOver, (await second).Role);
        Assert.Equal([a], await got.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_frozen_running_copy_lets_the_new_launch_open_its_own_window_and_never_takes_the_file()
    {
        var primary = Claim();
        var received = 0;
        primary.FilesReceived += _ => received++;
        primary.StartListening((_, wait) => { Thread.Sleep(wait); return false; }); // UI thread stuck

        var started = DateTime.UtcNow;
        var second = Claim(Pdf("a.pdf"));

        Assert.Equal(InstanceRole.Standalone, second.Role);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(3));
        Thread.Sleep(300);
        Assert.Equal(0, received);
    }

    [Fact]
    public void After_the_running_copy_ends_the_next_launch_is_the_running_copy()
    {
        var first = Claim();
        first.StartListening(RunNow);
        first.Dispose();

        Assert.Equal(InstanceRole.Primary, Claim().Role);
    }

    [Fact]
    public void No_running_copy_at_all_means_primary_without_waiting()
    {
        var started = DateTime.UtcNow;
        Assert.Equal(InstanceRole.Primary, Claim(Pdf("a.pdf")).Role);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task The_background_claim_gives_the_same_answer()
    {
        var first = await DesktopSingleInstance.ClaimAsync([], _app);
        _owned.Add(first);
        first.StartListening(RunNow);
        var second = await DesktopSingleInstance.ClaimAsync([Pdf("a.pdf")], _app);
        _owned.Add(second);
        Assert.Equal((InstanceRole.Primary, InstanceRole.HandedOver), (first.Role, second.Role));
    }

    [Theory]
    [InlineData(new byte[] { 2, 0, 0, 0, 0 })]                 // unknown version
    [InlineData(new byte[] { 1, 65, 0, 0, 0 })]                // 65 files > 64
    [InlineData(new byte[] { 1, 1, 0, 0, 0, 255, 255, 0, 0 })] // a path longer than allowed
    [InlineData(new byte[] { 1, 1, 0, 0, 0, 3, 0, 0, 0, (byte)'a', (byte)'.', (byte)'b' })] // not a full path
    [InlineData(new byte[] { 1, 2, 0, 0 })]                    // truncated
    [InlineData(new byte[] { 1, 0, 0, 0, 0 })]                 // no token length
    public async Task Malformed_hand_overs_are_refused(byte[] message) =>
        Assert.Null(await InstanceChannel.DecodeAsync(new MemoryStream(message), CancellationToken.None));

    [Fact]
    public async Task Message_round_trips()
    {
        string[] files = [Path.Combine(_dir, "a b.pdf"), Path.Combine(_dir, "தமிழ்.pdf")];
        var back = await InstanceChannel.DecodeAsync(new MemoryStream(InstanceChannel.Encode(files, "token-123")), CancellationToken.None);
        Assert.Equal(files, back!.Files);
        Assert.Equal("token-123", back.ActivationToken);
        Assert.Null((await InstanceChannel.DecodeAsync(new MemoryStream(InstanceChannel.Encode(files, null)), CancellationToken.None))!.ActivationToken);
    }

    // ── router ──────────────────────────────────────────────────────────────

    private sealed class Instance : IAppInstance
    {
        public event Action<IReadOnlyList<string>>? FilesReceived;

        public void Raise(params string[] files) => FilesReceived?.Invoke(files);
    }

    [Fact]
    public void Router_waits_for_the_first_render_then_opens_each_file_in_order()
    {
        var ready = new AppReadySignal();
        var open = new OpenDocumentService();
        var instance = new Instance();
        var router = new IncomingFiles(new BenchDropService(), open, ready, instance);
        var a = Pdf("a.pdf");
        var b = Pdf("b.docx");

        router.Receive([a, Path.Combine(_dir, "missing.pdf"), Pdf("notes.exe")]);
        instance.Raise(b);
        Assert.False(open.TryTake(out _)); // nothing before the first render

        ready.SignalReady();
        Assert.True(open.TryTake(out var first));
        Assert.True(open.TryTake(out var second));
        Assert.False(open.TryTake(out _));
        Assert.Equal((a, b), (first.Path, second.Path));
    }

    [Fact]
    public void Router_sends_files_to_the_Bench_when_it_is_open_and_brings_the_window_forward()
    {
        var ready = new AppReadySignal();
        ready.SignalReady();
        var bench = new BenchDropService();
        bench.SetBenchActive(true);
        IReadOnlyList<string>? dropped = null;
        bench.FilesDropped += f => dropped = f;
        var router = new IncomingFiles(bench, new OpenDocumentService(), ready, new Instance());
        var delivered = 0;
        router.Delivered += () => delivered++;
        var a = Pdf("a.pdf");

        router.Receive([a]);
        router.Receive([]); // a launch with no file: just come forward

        Assert.Equal([a], dropped);
        Assert.Equal(2, delivered);
    }
}
