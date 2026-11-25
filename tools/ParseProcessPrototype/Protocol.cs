using System.Buffers.Binary;
using System.Text;

namespace ParseProcessPrototype;

/// <summary>The wire between the app and the parse process: one request, one answer, in order.
/// A frame is [1 byte op or status][4 bytes length, little-endian][payload]. Text is UTF-8. Nothing else
/// crosses: no objects, no shared memory, so a crash on one side cannot corrupt the other.</summary>
public static class Wire
{
    public const byte Ping = 1, Open = 2, Render = 3, Close = 4, Word = 5, Excel = 6, StackOverflow = 7, Spin = 8,
        Stacks = 9, Allocate = 10, Echo = 11, Mem = 12, Quit = 0;
    public const byte Ok = 0x80, Failed = 0x81;

    public static void Write(Stream s, byte code, ReadOnlySpan<byte> payload)
    {
        Span<byte> head = stackalloc byte[5];
        head[0] = code;
        BinaryPrimitives.WriteInt32LittleEndian(head[1..], payload.Length);
        s.Write(head);
        s.Write(payload);
        s.Flush();
    }

    public static void Write(Stream s, byte code, string text) => Write(s, code, Encoding.UTF8.GetBytes(text));

    /// <summary>Null at the end of the stream (the other side has gone).</summary>
    public static (byte Code, byte[] Payload)? Read(Stream s)
    {
        Span<byte> head = stackalloc byte[5];
        if (!Fill(s, head)) return null;
        int n = BinaryPrimitives.ReadInt32LittleEndian(head[1..]);
        var payload = new byte[n];
        if (!Fill(s, payload)) return null;
        return (head[0], payload);
    }

    private static bool Fill(Stream s, Span<byte> buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = s.Read(buffer[got..]);
            if (n == 0) return false;
            got += n;
        }
        return true;
    }
}
