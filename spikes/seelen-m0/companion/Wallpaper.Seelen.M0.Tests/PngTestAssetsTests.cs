using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Wallpaper.Seelen.M0.Companion;

public sealed class PngTestAssetsTests
{
    [Fact]
    public void GeneratedAssets_HaveValidChunksAndRgbaScanlines()
    {
        AssertValidRgbaPng(PngTestAssets.Icon, 16, 16);
        AssertValidRgbaPng(PngTestAssets.Image, 64, 32);
    }

    private static void AssertValidRgbaPng(
        byte[] png,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            png[..8]);

        var offset = 8;
        var chunkTypes = new List<string>();
        using var compressed = new MemoryStream();

        while (offset < png.Length)
        {
            Assert.True(offset + 12 <= png.Length);
            var dataLength = BinaryPrimitives.ReadInt32BigEndian(
                png.AsSpan(offset, 4));
            Assert.True(dataLength >= 0);
            Assert.True(offset + 12 + dataLength <= png.Length);

            var type = png.AsSpan(offset + 4, 4);
            var data = png.AsSpan(offset + 8, dataLength);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                png.AsSpan(offset + 8 + dataLength, 4));

            Assert.Equal(CalculateCrc(type, data), storedCrc);

            var typeName = Encoding.ASCII.GetString(type);
            chunkTypes.Add(typeName);
            switch (typeName)
            {
                case "IHDR":
                    Assert.Equal(13, dataLength);
                    Assert.Equal(expectedWidth, BinaryPrimitives.ReadInt32BigEndian(data));
                    Assert.Equal(expectedHeight, BinaryPrimitives.ReadInt32BigEndian(data[4..]));
                    Assert.Equal(8, data[8]);
                    Assert.Equal(6, data[9]);
                    Assert.Equal(0, data[10]);
                    Assert.Equal(0, data[11]);
                    Assert.Equal(0, data[12]);
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    Assert.Equal(0, dataLength);
                    break;
            }

            offset += 12 + dataLength;
        }

        Assert.Equal(png.Length, offset);
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunkTypes);

        compressed.Position = 0;
        using var decompressor = new ZLibStream(
            compressed,
            CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        decompressor.CopyTo(scanlines);
        var pixels = scanlines.ToArray();
        var stride = (expectedWidth * 4) + 1;

        Assert.Equal(stride * expectedHeight, pixels.Length);
        for (var y = 0; y < expectedHeight; y++)
        {
            Assert.Equal(0, pixels[y * stride]);
        }
    }

    private static uint CalculateCrc(
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        var crc = UpdateCrc(0xffffffff, type);
        return UpdateCrc(crc, data) ^ 0xffffffff;
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
}
