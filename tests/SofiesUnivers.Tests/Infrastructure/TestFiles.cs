namespace SofiesUnivers.Tests.Infrastructure;

public static class TestFiles
{
    /// <summary>A tiny but valid JPEG header followed by filler bytes.</summary>
    public static byte[] Jpeg(int size = 2048, byte seed = 1)
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        for (var i = 4; i < size; i++)
        {
            bytes[i] = (byte)((i * 31 + seed) % 256);
        }

        return bytes;
    }

    public static byte[] Pdf(int size = 1500)
    {
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        for (var i = 9; i < size; i++)
        {
            bytes[i] = (byte)('a' + i % 26);
        }

        return bytes;
    }

    public static byte[] Heic()
    {
        var bytes = new byte[64];
        bytes[3] = 0x18;
        "ftypheic"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }
}
