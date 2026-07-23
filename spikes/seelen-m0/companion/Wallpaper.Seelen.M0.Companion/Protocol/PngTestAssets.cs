using System.Buffers.Binary;
using System.IO.Compression;

namespace Wallpaper.Seelen.M0.Companion;

internal static class PngTestAssets
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static byte[] Icon { get; } = CreateRgbaPng(16, 16, isIcon: true);

    public static byte[] Image { get; } = CreateRgbaPng(64, 32, isIcon: false);

    private static byte[] CreateRgbaPng(int width, int height, bool isIcon)
    {
        using var png = new MemoryStream();
        png.Write(PngSignature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(png, "IHDR"u8, header);

        using var scanlines = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            scanlines.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var pixel = isIcon
                    ? CreateIconPixel(x, y)
                    : CreateImagePixel(x, y, width, height);
                scanlines.WriteByte(pixel.Red);
                scanlines.WriteByte(pixel.Green);
                scanlines.WriteByte(pixel.Blue);
                scanlines.WriteByte(pixel.Alpha);
            }
        }

        scanlines.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
                   compressed,
                   CompressionLevel.SmallestSize,
                   leaveOpen: true))
        {
            scanlines.CopyTo(zlib);
        }

        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static RgbaPixel CreateIconPixel(int x, int y)
    {
        var distanceSquared = ((x * 2) - 15) * ((x * 2) - 15)
            + ((y * 2) - 15) * ((y * 2) - 15);
        if (distanceSquared > 190)
        {
            return new RgbaPixel(0, 0, 0, 0);
        }

        return (x + y) % 6 < 3
            ? new RgbaPixel(116, 151, 255, 255)
            : new RgbaPixel(237, 100, 255, 255);
    }

    private static RgbaPixel CreateImagePixel(
        int x,
        int y,
        int width,
        int height)
    {
        var red = (byte)(28 + (x * 180 / (width - 1)));
        var green = (byte)(38 + (y * 130 / (height - 1)));
        var blue = (byte)(210 - (x * 80 / (width - 1)));
        return new RgbaPixel(red, green, blue, 255);
    }

    private static void WriteChunk(
        Stream stream,
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(data);

        var crc = UpdateCrc(0xffffffff, type);
        crc = UpdateCrc(crc, data) ^ 0xffffffff;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        stream.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320 : 0);
            }
        }

        return crc;
    }

    private readonly record struct RgbaPixel(
        byte Red,
        byte Green,
        byte Blue,
        byte Alpha);
}
