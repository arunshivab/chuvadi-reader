using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace ChuvadiReader.Hosting.Desktop;

/// <summary>
/// The hand-over between two launches. Kept apart from <see cref="DesktopSingleInstance"/> so the pipe
/// and socket libraries load only when a hand-over happens or the running copy starts listening
/// (after its first paint) — never on a first launch's critical path.
/// <para>Message: version byte (1), file count (int32 LE), then per file its UTF-8 length (int32 LE) and
/// bytes, then the launcher's activation token the same way (length 0 = none; Wayland's
/// XDG_ACTIVATION_TOKEN, so the running window may come to the front); at most <see cref="MaxFiles"/>
/// files and <see cref="MaxBytes"/> bytes. Reply: one byte, 1 = the running copy's UI took the files,
/// 0 = refused (busy or invalid).</para>
/// </summary>
internal static class InstanceChannel
{
    public const byte Version = 1;
    public const int MaxFiles = 64;
    public const int MaxBytes = 64 * 1024;
    private const int MaxPathChars = 4096;

    /// <summary>Sends <paramref name="files"/> to the running copy. True only when it accepted them.</summary>
    public static bool TrySend(InstanceName name, IReadOnlyList<string> files, string? activationToken, TimeSpan connectWait, TimeSpan ackWait)
    {
        try
        {
            using var stream = Connect(name, connectWait);
            if (stream is null)
            {
                return false;
            }

            var message = Encode(files.Take(MaxFiles).ToArray(), activationToken);
            stream.Write(message);
            stream.Flush();

            using var cts = new CancellationTokenSource(ackWait);
            var reply = new byte[1];
            var read = stream.ReadAsync(reply, cts.Token).AsTask().GetAwaiter().GetResult();
            return read == 1 && reply[0] == 1;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or UnauthorizedAccessException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Starts the running copy's listener on a background thread. Dispose to stop.</summary>
    public static IDisposable Listen(InstanceName name, Func<HandOver, bool> deliver)
    {
        var cts = new CancellationTokenSource();
        _ = OperatingSystem.IsWindows()
            ? Task.Run(() => PipeLoopAsync(name, deliver, cts.Token))
            : Task.Run(() => SocketLoopAsync(name, deliver, cts.Token));
        return new Stopper(cts, OperatingSystem.IsWindows() ? null : name.SocketPath);
    }

    // ── message ─────────────────────────────────────────────────────────────

    public static byte[] Encode(IReadOnlyList<string> files, string? activationToken)
    {
        using var ms = new MemoryStream();
        Span<byte> n = stackalloc byte[4];
        ms.WriteByte(Version);
        BinaryPrimitives.WriteInt32LittleEndian(n, files.Count);
        ms.Write(n);
        foreach (var f in files)
        {
            var bytes = Encoding.UTF8.GetBytes(f);
            BinaryPrimitives.WriteInt32LittleEndian(n, bytes.Length);
            ms.Write(n);
            ms.Write(bytes);
        }

        var token = string.IsNullOrEmpty(activationToken) ? [] : Encoding.UTF8.GetBytes(activationToken);
        BinaryPrimitives.WriteInt32LittleEndian(n, token.Length);
        ms.Write(n);
        ms.Write(token);

        if (ms.Length > MaxBytes)
        {
            throw new IOException("Too much to hand over.");
        }

        return ms.ToArray();
    }

    /// <summary>Reads one message; null when it is malformed, too large or not this version.</summary>
    public static async Task<HandOver?> DecodeAsync(Stream stream, CancellationToken ct)
    {
        var head = new byte[5];
        if (!await ReadExactlyOrFalse(stream, head, ct) || head[0] != Version)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(1));
        if (count < 0 || count > MaxFiles)
        {
            return null;
        }

        var files = new List<string>(count);
        var total = head.Length;
        var len = new byte[4];
        for (var i = 0; i < count; i++)
        {
            if (!await ReadExactlyOrFalse(stream, len, ct))
            {
                return null;
            }

            var n = BinaryPrimitives.ReadInt32LittleEndian(len);
            total += 4 + n;
            if (n <= 0 || n > MaxPathChars * 4 || total > MaxBytes)
            {
                return null;
            }

            var bytes = new byte[n];
            if (!await ReadExactlyOrFalse(stream, bytes, ct))
            {
                return null;
            }

            var path = Encoding.UTF8.GetString(bytes);
            if (path.Length > MaxPathChars || !Path.IsPathFullyQualified(path))
            {
                return null;
            }

            files.Add(path);
        }

        if (!await ReadExactlyOrFalse(stream, len, ct))
        {
            return null;
        }

        var tokenLength = BinaryPrimitives.ReadInt32LittleEndian(len);
        total += 4 + tokenLength;
        if (tokenLength < 0 || tokenLength > 1024 || total > MaxBytes)
        {
            return null;
        }

        string? token = null;
        if (tokenLength > 0)
        {
            var bytes = new byte[tokenLength];
            if (!await ReadExactlyOrFalse(stream, bytes, ct))
            {
                return null;
            }

            token = Encoding.UTF8.GetString(bytes);
        }

        return new HandOver(files, token);
    }

    private static async Task<bool> ReadExactlyOrFalse(Stream s, byte[] buffer, CancellationToken ct)
    {
        try
        {
            await s.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    // ── client ──────────────────────────────────────────────────────────────

    private static Stream? Connect(InstanceName name, TimeSpan wait)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", name.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                pipe.Connect((int)wait.TotalMilliseconds); // waits while the running copy is still starting
                return pipe;
            }
            catch (TimeoutException)
            {
                pipe.Dispose();
                return null;
            }
        }

        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(name.SocketPath));
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                socket.Dispose();
                Thread.Sleep(50); // not listening yet (still starting)
            }
            catch (SocketException)
            {
                socket.Dispose();
                return null;
            }
        }
    }

    // ── server ──────────────────────────────────────────────────────────────

    private static async Task PipeLoopAsync(InstanceName name, Func<HandOver, bool> deliver, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    name.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(server, deliver, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(200, ct).ConfigureAwait(false); // a broken client; keep serving
            }
        }
    }

    private static async Task SocketLoopAsync(InstanceName name, Func<HandOver, bool> deliver, CancellationToken ct)
    {
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            File.Delete(name.SocketPath); // stale after a crash; we hold the lock, so nobody else owns it
            listener.Bind(new UnixDomainSocketEndPoint(name.SocketPath));
            listener.Listen(8);
        }
        catch (Exception e) when (e is IOException or SocketException or UnauthorizedAccessException)
        {
            return; // can't listen: later launches open their own windows, as before
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptAsync(ct).ConfigureAwait(false);
                await using var stream = new NetworkStream(client, ownsSocket: false);
                await ServeAsync(stream, deliver, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or SocketException)
            {
                // a broken client; keep serving
            }
        }
    }

    private static async Task ServeAsync(Stream stream, Func<HandOver, bool> deliver, CancellationToken ct)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
        read.CancelAfter(TimeSpan.FromSeconds(2));
        var handOver = await DecodeAsync(stream, read.Token).ConfigureAwait(false);
        var accepted = handOver is not null && deliver(handOver);
        try
        {
            await stream.WriteAsync(new[] { accepted ? (byte)1 : (byte)0 }, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The new launch gave up already and opened its own window. Normally deliver() returned
            // false first (UiWait < AckWait), so the files weren't taken here either.
        }
    }

    private sealed class Stopper(CancellationTokenSource cts, string? socketPath) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            if (socketPath is not null)
            {
                try { File.Delete(socketPath); } catch (IOException) { }
            }
        }
    }
}

/// <summary>One hand-over: the files (full paths, in order) and the launcher's activation token, if any.</summary>
internal sealed record HandOver(IReadOnlyList<string> Files, string? ActivationToken);
