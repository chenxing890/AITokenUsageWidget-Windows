using System.IO.Compression;
using AITokenUsageWidget.Shared.Widgets;
using Xunit;

namespace AITokenUsageWidget.Shared.Tests;

public class PngBarTests
{
    private static readonly PngBar.Rgb Fill = new(255, 69, 0);
    private static readonly PngBar.Rgb Track = new(229, 229, 229);

    private static byte[] Decode(string dataUri)
    {
        const string prefix = "data:image/png;base64,";
        Assert.StartsWith(prefix, dataUri);
        return Convert.FromBase64String(dataUri[prefix.Length..]);
    }

    [Fact]
    public void DataUri_ProducesValidPngStructure()
    {
        var png = Decode(PngBar.DataUri(120, 8, 50, Fill, Track));

        // PNG 签名
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        // IHDR：宽高 + RGBA8
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png[12..16]));
        Assert.Equal(120, ReadBE(png, 16));
        Assert.Equal(8, ReadBE(png, 20));
        Assert.Equal(8, png[24]); // bit depth
        Assert.Equal(6, png[25]); // color type: RGBA
        // IEND 收尾
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png[^8..^4]));
    }

    [Fact]
    public void Pixels_FillAndTrackMatchPercent()
    {
        const int w = 100, h = 8;
        var pixels = InflateIdat(Decode(PngBar.DataUri(w, h, 50, Fill, Track)), w, h);

        // 左半（填充区中心）≈ 填充色，右半（轨道区中心）≈ 轨道色
        AssertColor(Fill, Pixel(pixels, w, 25, h / 2));
        AssertColor(Track, Pixel(pixels, w, 75, h / 2));
    }

    [Fact]
    public void Pixels_ZeroAndHundredPercent()
    {
        const int w = 60, h = 6;
        var empty = InflateIdat(Decode(PngBar.DataUri(w, h, 0, Fill, Track)), w, h);
        AssertColor(Track, Pixel(empty, w, 5, h / 2)); // 0%：最左也是轨道色

        var full = InflateIdat(Decode(PngBar.DataUri(w, h, 100, Fill, Track)), w, h);
        AssertColor(Fill, Pixel(full, w, w - 5, h / 2)); // 100%：最右也是填充色
    }

    [Fact]
    public void Pixels_PercentIsClamped()
    {
        // 越界百分比不抛异常，按 0/100 渲染
        var png = Decode(PngBar.DataUri(40, 4, 250, Fill, Track));
        Assert.Equal(40, ReadBE(png, 16));
    }

    // ---- 解码辅助 ----

    private static int ReadBE(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    /// <summary>取第一个 IDAT 块并 zlib 解压为 scanlines（每行 1 字节 filter + RGBA 像素）。</summary>
    private static byte[] InflateIdat(byte[] png, int width, int height)
    {
        var offset = 8;
        while (offset < png.Length)
        {
            var length = ReadBE(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                using var input = new MemoryStream(png, offset + 8, length);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                var scanlines = output.ToArray();
                Assert.Equal(height * (1 + width * 4), scanlines.Length);
                return scanlines;
            }
            offset += 12 + length;
        }
        throw new InvalidOperationException("未找到 IDAT 块");
    }

    private static (byte r, byte g, byte b, byte a) Pixel(byte[] scanlines, int width, int x, int y)
    {
        var i = y * (1 + width * 4) + 1 + x * 4;
        return (scanlines[i], scanlines[i + 1], scanlines[i + 2], scanlines[i + 3]);
    }

    private static void AssertColor(PngBar.Rgb expected, (byte r, byte g, byte b, byte a) actual)
    {
        Assert.Equal(255, actual.a); // 胶囊内部像素不透明
        Assert.Equal(expected.R, actual.r);
        Assert.Equal(expected.G, actual.g);
        Assert.Equal(expected.B, actual.b);
    }
}
