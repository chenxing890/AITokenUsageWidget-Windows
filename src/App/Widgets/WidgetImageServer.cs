using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AITokenUsageWidget.Shared.Models;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// Provider 进程内嵌的本地图片服务（http://127.0.0.1）。
/// 背景：实测 Windows Widgets Board 不渲染任何 data: URI 图片（探针卡片 + 像素扫描），
/// 但能正常加载 http(s) 图片；且 Container 的 backgroundImage 会被拉伸成「首行高度 ×
/// 内容宽度」的横带，无法整体铺底。因此每个供应商卡片整体渲染为一张 PNG：
/// - /pcard?w=逻辑宽&amp;d=base64url(JSON) → macOS 风格供应商卡片 PNG
///   （圆角浅底 + padding + 品牌图标 + 名称 + 余额/窗口行 + 胶囊进度条，2x 超采样）
/// 仅绑定 loopback，不对外暴露；URL 携带全部渲染数据，数据变则 URL 变，Board 缓存安全。
/// </summary>
public static class WidgetImageServer
{
    private const int FirstPort = 49231;
    private const int LastPort = 49240;

    private static HttpListener? _listener;
    private static readonly ConcurrentDictionary<string, byte[]> ImageCache = new();

    /// <summary>服务基地址（未启动时为 null，卡片退化为纯文本渲染）。</summary>
    public static string? BaseUrl { get; private set; }

    public static void Start()
    {
        if (_listener != null) return;
        for (var port = FirstPort; port <= LastPort; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                BaseUrl = $"http://127.0.0.1:{port}";
                Listen(listener);
                return;
            }
            catch (HttpListenerException)
            {
                listener.Close(); // 端口被占用，尝试下一个
            }
        }
        // 全部端口不可用：BaseUrl 保持 null，卡片走文本兜底
    }

    private static async void Listen(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                break; // 监听器已停止
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    private static void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var query = context.Request.Url?.Query ?? "";
            byte[]? body = path == "/pcard" ? RenderProviderCard(query) : null;
            if (body == null)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
            context.Response.ContentType = "image/png";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
        catch (Exception)
        {
            try { context.Response.Abort(); } catch { }
        }
    }

    /// <summary>
    /// 供应商卡片整卡 PNG。载荷 d = base64url(JSON)：{k,n,d,msg,err,big,sub,max,rows[{l,r,p}]}，
    /// 全部文本由卡片构建端预格式化（服务端不感知 L10n）。2x 超采样渲染，Board 按
    /// Image size=stretch 等比缩放到实际列宽。w 为逻辑宽度。
    /// </summary>
    private static byte[]? RenderProviderCard(string query)
    {
        var w = Math.Clamp(GetInt(query, "w") ?? 340, 96, 480);
        var d = GetRaw(query, "d");
        if (string.IsNullOrEmpty(d)) return null;
        try
        {
            return ImageCache.GetOrAdd($"pcard|{w}|{d}", _ => DrawProviderCard(w, d));
        }
        catch (Exception)
        {
            return null; // 载荷损坏 → 404，卡片留白（下次刷新自愈）
        }
    }

    // 逻辑像素布局常量（绘制时 ×2 超采样）
    private const float PadX = 12f, PadTop = 9f, PadBottom = 9f;
    private const float IconSize = 18f, HeaderH = 20f, HeaderGap = 5f;
    private const float BigH = 27f, SubH = 15f, MaxH = 23f;
    private const float RowLineH = 15f, BarGap = 3f, RowGap = 6f, PlainRowGap = 4f;

    private static byte[] DrawProviderCard(int width, string d)
    {
        using var doc = JsonDocument.Parse(Convert.FromBase64String(Base64Pad(d)));
        var root = doc.RootElement;
        var dark = root.TryGetProperty("d", out var darkEl) && darkEl.GetInt32() == 1;
        var kind = root.TryGetProperty("k", out var kEl) ? kEl.GetString() : "";
        var name = root.TryGetProperty("n", out var nEl) ? nEl.GetString() ?? "" : "";
        var msg = root.TryGetProperty("msg", out var mEl) ? mEl.GetString() : null;
        var isErr = root.TryGetProperty("err", out var eEl) && eEl.GetInt32() == 1;
        var big = root.TryGetProperty("big", out var bEl) ? bEl.GetString() : null;
        var sub = root.TryGetProperty("sub", out var sEl) ? sEl.GetString() : null;
        var max = root.TryGetProperty("max", out var xEl) ? xEl.GetString() : null;
        var dense = width <= 120;
        var barH = dense ? 3f : 4f;
        var innerW = width - PadX * 2;

        const float s = 2f; // 超采样倍率
        using var fName = new System.Drawing.Font("Segoe UI", 12f * s, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var fBig = new System.Drawing.Font("Segoe UI", 20f * s, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var fMax = new System.Drawing.Font("Segoe UI", 17f * s, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var fLine = new System.Drawing.Font("Segoe UI", 10.5f * s, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);

        // msg 可能换行：先用测量画布算高度
        var msgH = 0f;
        if (msg != null)
        {
            using var measureBmp = new System.Drawing.Bitmap(1, 1);
            using var mg = System.Drawing.Graphics.FromImage(measureBmp);
            var size = mg.MeasureString(msg, fLine, new System.Drawing.SizeF(innerW * s, 1000),
                System.Drawing.StringFormat.GenericTypographic);
            msgH = (float)Math.Ceiling(size.Height / s);
        }

        var rows = root.TryGetProperty("rows", out var rowsEl)
            ? rowsEl.EnumerateArray().ToList()
            : [];

        // 高度预算（与绘制 pass 保持同一套增量）
        var h = PadTop + HeaderH + HeaderGap;
        if (msg != null) h += msgH + 4;
        if (big != null) h += BigH;
        if (sub != null) h += SubH;
        if (max != null) h += MaxH;
        foreach (var row in rows)
        {
            h += RowLineH + (row.TryGetProperty("p", out _) ? BarGap + barH + RowGap : PlainRowGap);
        }
        h += PadBottom;
        var heightPx = (int)Math.Ceiling(h * s);
        var widthPx = (int)Math.Ceiling(width * s);

        using var bmp = new System.Drawing.Bitmap(widthPx, heightPx);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            var textColor = dark
                ? System.Drawing.Color.FromArgb(245, 245, 245)
                : System.Drawing.Color.FromArgb(28, 28, 28);
            var subtleColor = dark
                ? System.Drawing.Color.FromArgb(165, 255, 255, 255)
                : System.Drawing.Color.FromArgb(160, 55, 55, 55);
            using var textBrush = new System.Drawing.SolidBrush(textColor);
            using var subtleBrush = new System.Drawing.SolidBrush(subtleColor);
            using var errBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(232, 90, 90));

            // 卡片底：浅底圆角矩形（对齐 macOS containerBackground）
            using (var cardPath = RoundedRect(0.5f * s, 0.5f * s, widthPx - s, heightPx - s, 8f * s))
            using (var cardBrush = new System.Drawing.SolidBrush(dark
                       ? System.Drawing.Color.FromArgb(26, 255, 255, 255)
                       : System.Drawing.Color.FromArgb(18, 0, 0, 0)))
            {
                g.FillPath(cardBrush, cardPath);
            }

            using var fmtNear = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic)
            {
                Alignment = System.Drawing.StringAlignment.Near,
                LineAlignment = System.Drawing.StringAlignment.Center,
                FormatFlags = System.Drawing.StringFormatFlags.NoWrap,
                Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            };
            using var fmtFar = (System.Drawing.StringFormat)fmtNear.Clone();
            fmtFar.Alignment = System.Drawing.StringAlignment.Far;
            using var fmtWrap = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic)
            {
                Alignment = System.Drawing.StringAlignment.Near,
                LineAlignment = System.Drawing.StringAlignment.Near,
            };

            float y = PadTop;
            // 头部：品牌图标 + 名称
            using (var iconStream = new System.IO.MemoryStream(DrawIcon(KindOf(kind), (int)(IconSize * s))))
            using (var iconBmp = new System.Drawing.Bitmap(iconStream))
            {
                g.DrawImage(iconBmp, PadX * s, (y + (HeaderH - IconSize) / 2) * s, IconSize * s, IconSize * s);
            }
            g.DrawString(name, fName, textBrush,
                new System.Drawing.RectangleF((PadX + IconSize + 7) * s, y * s, (innerW - IconSize - 7) * s, HeaderH * s), fmtNear);
            y += HeaderH + HeaderGap;

            if (msg != null)
            {
                g.DrawString(msg, fLine, isErr ? errBrush : subtleBrush,
                    new System.Drawing.RectangleF(PadX * s, y * s, innerW * s, msgH * s), fmtWrap);
                y += msgH + 4;
            }
            if (big != null)
            {
                g.DrawString(big, fBig, textBrush,
                    new System.Drawing.RectangleF(PadX * s, y * s, innerW * s, BigH * s), fmtNear);
                y += BigH;
            }
            if (sub != null)
            {
                g.DrawString(sub, fLine, subtleBrush,
                    new System.Drawing.RectangleF(PadX * s, y * s, innerW * s, SubH * s), fmtNear);
                y += SubH;
            }
            if (max != null)
            {
                g.DrawString(max, fMax, textBrush,
                    new System.Drawing.RectangleF(PadX * s, y * s, innerW * s, MaxH * s), fmtNear);
                y += MaxH;
            }

            foreach (var row in rows)
            {
                var left = row.TryGetProperty("l", out var lEl) ? lEl.GetString() ?? "" : "";
                var lineRect = new System.Drawing.RectangleF(PadX * s, y * s, innerW * s, RowLineH * s);
                g.DrawString(left, fLine, subtleBrush, lineRect, fmtNear);
                if (row.TryGetProperty("r", out var rEl) && rEl.GetString() is { } right)
                {
                    g.DrawString(right, fLine, textBrush, lineRect, fmtFar);
                }
                y += RowLineH;
                if (row.TryGetProperty("p", out var pEl))
                {
                    var percent = Math.Clamp(pEl.GetDouble(), 0, 100);
                    y += BarGap;
                    using (var trackPath = Capsule(PadX * s, y * s, innerW * s, barH * s, barH * s / 2))
                    using (var trackBrush = new System.Drawing.SolidBrush(dark
                               ? System.Drawing.Color.FromArgb(30, 255, 255, 255)
                               : System.Drawing.Color.FromArgb(22, 0, 0, 0)))
                    {
                        g.FillPath(trackBrush, trackPath);
                    }
                    var fillW = (float)(innerW * s * percent / 100.0);
                    if (fillW > 0)
                    {
                        using var fillPath = Capsule(PadX * s, y * s, Math.Max(fillW, barH * s), barH * s, barH * s / 2);
                        using var fillBrush = new System.Drawing.SolidBrush(FillFor(percent));
                        g.FillPath(fillBrush, fillPath);
                    }
                    y += barH + RowGap;
                }
                else
                {
                    y += PlainRowGap;
                }
            }
        }
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static ProviderKind KindOf(string? k) => k switch
    {
        "deepseek" => ProviderKind.DeepSeek,
        "kimi" => ProviderKind.Kimi,
        _ => ProviderKind.Glm,
    };

    private static string Base64Pad(string d)
    {
        var b64 = d.Replace('-', '+').Replace('_', '/');
        return (b64.Length % 4) switch
        {
            2 => b64 + "==",
            3 => b64 + "=",
            _ => b64,
        };
    }

    private static System.Drawing.Drawing2D.GraphicsPath Capsule(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 90, 180);
        path.AddArc(x + w - d, y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 品牌图标 PNG：与主 App ProviderChip 同一套图形（品牌色对角渐变圆角方块 +
    /// 白色字形：DeepSeek 水滴 / Kimi 月亮 / GLM 星芒）。按目标尺寸直接矢量绘制——
    /// 从 96px 原图重采样会糊边丢细节，直接画最锐利。
    /// </summary>
    private static byte[] DrawIcon(ProviderKind kind, int size)
    {
        var hex = kind.BrandHex();
        var cr = Convert.ToInt32(hex.Substring(1, 2), 16);
        var cg = Convert.ToInt32(hex.Substring(3, 2), 16);
        var cb = Convert.ToInt32(hex.Substring(5, 2), 16);
        var f = size / 96f; // 字形坐标基于 96px 设计稿等比缩放

        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // 圆角方块底：品牌色 → 62% 不透明度的对角渐变（对齐 macOS accentColor → 0.62）
            using (var rectPath = RoundedRect(0f, 0f, size, size, 28f * f))
            using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                       new System.Drawing.PointF(0, 0), new System.Drawing.PointF(size, size),
                       System.Drawing.Color.FromArgb(255, cr, cg, cb),
                       System.Drawing.Color.FromArgb(158, cr, cg, cb)))
            {
                g.FillPath(grad, rectPath);
            }
            using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            switch (kind)
            {
                case ProviderKind.DeepSeek: // 水滴
                    using (var drop = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        drop.AddEllipse(28 * f, 36 * f, 40 * f, 40 * f);
                        g.FillPath(white, drop);
                    }
                    using (var tip = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        tip.AddPolygon(new[]
                        {
                            new System.Drawing.PointF(48 * f, 16 * f),
                            new System.Drawing.PointF(66 * f, 54 * f),
                            new System.Drawing.PointF(30 * f, 54 * f),
                        });
                        g.FillPath(white, tip);
                    }
                    break;
                case ProviderKind.Kimi: // 月亮 + 四角星
                    using (var outer = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        outer.AddEllipse(22 * f, 22 * f, 52 * f, 52 * f);
                        using var inner = new System.Drawing.Drawing2D.GraphicsPath();
                        inner.AddEllipse(38 * f, 14 * f, 46 * f, 46 * f);
                        using var region = new System.Drawing.Region(outer);
                        region.Exclude(inner);
                        g.FillRegion(white, region);
                    }
                    using (var star = Star4(68 * f, 30 * f, 9 * f, 3 * f))
                    {
                        g.FillPath(white, star);
                    }
                    break;
                default: // GLM 星芒（大小两颗四角星）
                    using (var big = Star4(42 * f, 44 * f, 30 * f, 9 * f))
                    {
                        g.FillPath(white, big);
                    }
                    using (var small = Star4(70 * f, 66 * f, 15 * f, 5 * f))
                    {
                        g.FillPath(white, small);
                    }
                    break;
            }
        }
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static System.Drawing.Drawing2D.GraphicsPath Star4(float cx, float cy, float rOuter, float rInner)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddPolygon(new[]
        {
            new System.Drawing.PointF(cx, cy - rOuter),
            new System.Drawing.PointF(cx + rInner, cy - rInner),
            new System.Drawing.PointF(cx + rOuter, cy),
            new System.Drawing.PointF(cx + rInner, cy + rInner),
            new System.Drawing.PointF(cx, cy + rOuter),
            new System.Drawing.PointF(cx - rInner, cy + rInner),
            new System.Drawing.PointF(cx - rOuter, cy),
            new System.Drawing.PointF(cx - rInner, cy - rInner),
        });
        path.CloseFigure();
        return path;
    }

    private static System.Drawing.Color FillFor(double percent) => percent switch
    {
        >= 80 => System.Drawing.Color.FromArgb(255, 69, 0),   // OrangeRed
        >= 50 => System.Drawing.Color.FromArgb(255, 140, 0),  // DarkOrange
        _ => System.Drawing.Color.FromArgb(60, 179, 113),     // MediumSeaGreen
    };

    private static int? GetInt(string query, string name) =>
        int.TryParse(GetRaw(query, name), out var i) ? i : null;

    private static string? GetRaw(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name) return kv[1];
        }
        return null;
    }
}
