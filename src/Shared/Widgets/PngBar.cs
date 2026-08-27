using System.IO.Compression;

namespace AITokenUsageWidget.Shared.Widgets;

/// <summary>
/// 圆角细进度条 PNG 生成器（macOS 风格胶囊条，对齐主界面 UsageCardControl.ProgressTrack）。
/// 纯 BCL 实现（Deflate + 手写 CRC32），无 System.Drawing 依赖，net8.0 可用。
/// Widgets Board 的 Adaptive Cards 没有原生进度条，Unicode 块字符（▰▱）渲染丑且不一致，
/// 按百分比运行时生成 2x 高清 PNG 内联 data URI，配合 Image.size=Stretch 等比缩放。
/// </summary>
public static class PngBar
{
    /// <summary>生成胶囊进度条 PNG 的 data URI。percent 自动夹取 0–100。</summary>
    public static string DataUri(int width, int height, double percent, Rgb fill, Rgb track)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var pixels = Render(width, height, clamped, fill, track);
        var png = Encode(pixels, width, height);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public readonly record struct Rgb(byte R, byte G, byte B);

    // ---- 像素渲染（SDF 抗锯齿胶囊） ----

    private static byte[] Render(int width, int height, double percent, Rgb fill, Rgb track)
    {
        var rgba = new byte[width * height * 4];
        var fillWidth = Math.Round(width * percent / 100.0);
        var radius = height / 2.0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var trackCoverage = CapsuleCoverage(x + 0.5, y + 0.5, width, radius);
                var fillCoverage = fillWidth >= 1
                    ? CapsuleCoverage(x + 0.5, y + 0.5, fillWidth, radius)
                    : 0;
                var i = (y * width + x) * 4;

                // 先铺轨道色，再叠填充色（两者同胶囊形状，fill 覆盖左段）
                (var r, var g, var b, var a) = Blend(track, trackCoverage);
                (r, g, b, a) = BlendOver((r, g, b, a), fill, fillCoverage);

                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = a;
            }
        }
        return rgba;
    }

    /// <summary>胶囊（圆角矩形，宽 w 高 2r）的有符号距离 → [0,1] 覆盖率（1px 抗锯齿）。</summary>
    private static double CapsuleCoverage(double px, double py, double w, double r)
    {
        if (w <= 0) return 0;
        var halfH = r;
        var cy = Math.Abs(py - halfH) - (halfH - r); // 中心线距离（矩形段）
        var cx = Math.Abs(px - w / 2) - (w / 2 - r);
        var dx = Math.Max(cx, 0);
        var dy = Math.Max(cy, 0);
        var dist = Math.Sqrt(dx * dx + dy * dy) + Math.Min(Math.Max(cx, cy), 0);
        return Math.Clamp(r - dist + 0.5, 0, 1);
    }

    private static (byte r, byte g, byte b, byte a) Blend(Rgb color, double alpha) =>
        (color.R, color.G, color.B, (byte)Math.Round(alpha * 255));

    /// <summary>标准 source-over alpha 合成。</summary>
    private static (byte r, byte g, byte b, byte a) BlendOver(
        (byte r, byte g, byte b, byte a) back, Rgb front, double frontAlpha)
    {
        if (frontAlpha <= 0) return back;
        var ba = back.a / 255.0;
        var outA = frontAlpha + ba * (1 - frontAlpha);
        if (outA <= 0) return (0, 0, 0, 0);
        return (
            (byte)Math.Round((front.R * frontAlpha + back.r * ba * (1 - frontAlpha)) / outA),
            (byte)Math.Round((front.G * frontAlpha + back.g * ba * (1 - frontAlpha)) / outA),
            (byte)Math.Round((front.B * frontAlpha + back.b * ba * (1 - frontAlpha)) / outA),
            (byte)Math.Round(outA * 255));
    }

    // ---- PNG 编码（RGBA8 + filter 0 + zlib） ----

    private static byte[] Encode(byte[] rgba, int width, int height)
    {
        var scanlines = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            var src = y * width * 4;
            var dst = y * (1 + width * 4);
            scanlines[dst] = 0; // filter: None（纯色 + 抗锯齿边缘，压缩率足够）
            Array.Copy(rgba, src, scanlines, dst + 1, width * 4);
        }

        using var idat = new MemoryStream();
        // ZLibStream（net6+）输出完整 zlib 流：0x78 头 + deflate + Adler32 校验尾
        using (var zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines, 0, scanlines.Length);
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", Ihdr(width, height));
        WriteChunk(png, "IDAT", idat.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] Ihdr(int width, int height)
    {
        var data = new byte[13];
        WriteBE(data, 0, width);
        WriteBE(data, 4, height);
        data[8] = 8;  // bit depth
        data[9] = 6;  // color type: RGBA
        data[10] = 0; // compression
        data[11] = 0; // filter
        data[12] = 0; // interlace
        return data;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var header = new byte[8];
        WriteBE(header, 0, data.Length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        Array.Copy(typeBytes, 0, header, 4, 4);
        stream.Write(header);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Array.Copy(typeBytes, crcInput, typeBytes.Length);
        Array.Copy(data, 0, crcInput, typeBytes.Length, data.Length);

        stream.Write(data);
        var crc = new byte[4];
        WriteBE(crc, 0, unchecked((int)Crc32(crcInput)));
        stream.Write(crc);
    }

    private static void WriteBE(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc >> 1) ^ (0xEDB88320 & (crc & 1));
        }
        return crc ^ 0xFFFFFFFF;
    }
}
