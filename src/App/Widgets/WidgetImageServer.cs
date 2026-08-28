using System.Collections.Concurrent;
using System.Net;
using AITokenUsageWidget.Shared.Models;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// Provider 进程内嵌的本地图片服务（http://127.0.0.1）。
/// 背景：实测 Windows Widgets Board 不渲染任何 data: URI 图片（探针卡片 + 像素扫描），
/// 但能正常加载 http(s) 图片。因此在 Provider 进程内监听 127.0.0.1，为卡片提供：
/// - /bar?p=0-100&amp;dark=0|1&amp;w=px&amp;h=px → 运行时生成的 macOS 风格胶囊进度条 PNG
///   （填充色按用量级别 ≥80 红 / ≥50 橙 / 其余绿，轨道色随系统明暗，与 UsageCardControl 同色板）
/// - /icon/{kind}?s=18|14 → 运行时矢量绘制的品牌图标 PNG（与 ProviderChip 同图形）
/// 仅绑定 loopback，不对外暴露；URL 携带全部渲染参数，参数变则 URL 变，Board 缓存安全。
/// </summary>
public static class WidgetImageServer
{
    private const int FirstPort = 49231;
    private const int LastPort = 49240;

    private static HttpListener? _listener;
    private static readonly ConcurrentDictionary<string, byte[]> BarCache = new();

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
            byte[]? body = path switch
            {
                "/bar" => RenderBar(query),
                "/cardbg" => RenderCardBg(query),
                "/icon/deepseek" or "/icon/kimi" or "/icon/glm" => RenderIcon(path, query),
                _ => null,
            };
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
    /// 胶囊进度条 PNG（macOS 风格，与 UsageCardControl.ProgressTrack 一致）：
    /// System.Drawing 标准编码（Board 渲染管线对手写 PNG 编码器兼容性未知，用
    /// 最标准的 BGRA32 PNG 排除兼容性问题）。填充色 = 用量级别色（同 LevelBrush）。
    /// </summary>
    private static byte[]? RenderBar(string query)
    {
        var p = GetDouble(query, "p") ?? 0;
        var dark = GetInt(query, "dark") == 1;
        var w = Math.Clamp(GetInt(query, "w") ?? 140, 8, 960);
        var h = Math.Clamp(GetInt(query, "h") ?? 4, 2, 32);

        var key = $"{p:F1}|{(dark ? 1 : 0)}|{w}|{h}";
        return BarCache.GetOrAdd(key, _ => DrawBar(w, h, Math.Clamp(p, 0, 100), dark));
    }

    /// <summary>
    /// 供应商卡片背景分段 PNG（seg=top|mid|bottom）。实测 Board 把 Container 的
    /// backgroundImage 拉伸成「首行高度 × 内容宽度」的横带（与图片固有尺寸无关），
    /// 因此卡片按行拆成多个容器堆叠：顶段圆角在上、底段圆角在下、中段直角。
    /// dark=白 7% / light=黑 5%，对齐 macOS containerBackground 的浅底色卡片。
    /// </summary>
    private static byte[] RenderCardBg(string query)
    {
        var dark = GetInt(query, "dark") == 1;
        var seg = GetRaw(query, "seg") ?? "mid";
        var key = $"cardbg|{(dark ? 1 : 0)}|{seg}";
        return BarCache.GetOrAdd(key, _ =>
        {
            const int w = 200;
            var h = seg == "mid" ? 12 : 24;
            const float radius = 7f;
            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var color = dark
                    ? System.Drawing.Color.FromArgb(22, 255, 255, 255)
                    : System.Drawing.Color.FromArgb(16, 0, 0, 0);
                using var brush = new System.Drawing.SolidBrush(color);
                if (seg == "mid")
                {
                    g.FillRectangle(brush, 0, 0, w, h);
                }
                else
                {
                    using var path = RoundedRectSelective(0.5f, 0.5f, w - 1, h - 1,
                        topRadius: seg == "top" ? radius : 0f,
                        bottomRadius: seg == "bottom" ? radius : 0f);
                    g.FillPath(brush, path);
                }
            }
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        });
    }

    /// <summary>只有上/下两个角带圆角的矩形路径（卡片顶段/底段）。</summary>
    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectSelective(
        float x, float y, float w, float h, float topRadius, float bottomRadius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var dt = topRadius * 2;
        var db = bottomRadius * 2;
        if (topRadius > 0)
        {
            path.AddArc(x, y, dt, dt, 180, 90); // 左上
            path.AddArc(x + w - dt, y, dt, dt, 270, 90); // 右上
        }
        else
        {
            path.AddLine(x, y, x + w, y);
        }
        if (bottomRadius > 0)
        {
            path.AddArc(x + w - db, y + h - db, db, db, 0, 90); // 右下
            path.AddArc(x, y + h - db, db, db, 90, 90); // 左下
        }
        else
        {
            path.AddLine(x + w, y + (topRadius > 0 ? topRadius : 0), x + w, y + h);
            path.AddLine(x + w, y + h, x, y + h);
        }
        path.CloseFigure();
        return path;
    }

    private static byte[] DrawBar(int width, int height, double percent, bool dark)
    {
        using var bmp = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var radius = height / 2f;
            // 轨道（整宽胶囊）
            using (var trackPath = Capsule(0, 0, width, height, radius))
            using (var trackBrush = new System.Drawing.SolidBrush(TrackFor(dark)))
            {
                g.FillPath(trackBrush, trackPath);
            }
            // 填充（按百分比的胶囊）
            var fillWidth = (float)Math.Round(width * percent / 100.0);
            if (fillWidth > 0)
            {
                using var fillPath = Capsule(0, 0, Math.Max(fillWidth, height), height, radius);
                using var fillBrush = new System.Drawing.SolidBrush(FillFor(percent));
                g.FillPath(fillBrush, fillPath);
            }
        }
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
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
    /// 白色字形：DeepSeek 水滴 / Kimi 月亮 / GLM 星芒）。按请求尺寸直接矢量绘制——
    /// 从 96px 原图重采样会糊边丢细节，直接画最锐利；固有尺寸 == 声明尺寸，杜绝裁剪。
    /// </summary>
    private static byte[] RenderIcon(string path, string query)
    {
        var size = GetInt(query, "s") == 14 ? 14 : 18;
        var kind = path switch
        {
            "/icon/deepseek" => ProviderKind.DeepSeek,
            "/icon/kimi" => ProviderKind.Kimi,
            _ => ProviderKind.Glm,
        };
        var key = $"icon|{kind}|{size}";
        return BarCache.GetOrAdd(key, _ => DrawIcon(kind, size));
    }

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

    private static System.Drawing.Color TrackFor(bool dark) =>
        dark ? System.Drawing.Color.FromArgb(60, 60, 60) : System.Drawing.Color.FromArgb(229, 229, 229);

    private static double? GetDouble(string query, string name)
    {
        var value = GetRaw(query, name);
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

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
