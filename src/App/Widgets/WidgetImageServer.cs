using System.Collections.Concurrent;
using System.Net;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Widgets;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// Provider 进程内嵌的本地图片服务（http://127.0.0.1）。
/// 背景：实测 Windows Widgets Board 不渲染任何 data: URI 图片（探针卡片 + 像素扫描），
/// 但能正常加载 http(s) 图片。因此在 Provider 进程内监听 127.0.0.1，为卡片提供：
/// - /bar?p=0-100&amp;dark=0|1&amp;w=px&amp;h=px → 运行时生成的 macOS 风格胶囊进度条 PNG
///   （填充色按用量级别 ≥80 红 / ≥50 橙 / 其余绿，轨道色随系统明暗，与 UsageCardControl 同色板）
/// - /icon/{kind}?s=18|14 → 预生成的品牌图标 PNG（固有尺寸 == 声明尺寸，杜绝裁剪）
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
    /// 供应商卡片背景：圆角半透明矩形 PNG（dark=白 7% / light=黑 5%，对齐 macOS
    /// containerBackground 的浅底色卡片）。Board 的 emphasis 底色几乎不可见，
    /// 用 backgroundImage + fillMode=Stretch 实现可见的模型区域划分。
    /// </summary>
    private static byte[] RenderCardBg(string query)
    {
        var dark = GetInt(query, "dark") == 1;
        var key = $"cardbg|{(dark ? 1 : 0)}";
        return BarCache.GetOrAdd(key, _ =>
        {
            const int size = 96;
            const float radius = 6f;
            using var bmp = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var color = dark
                    ? System.Drawing.Color.FromArgb(18, 255, 255, 255)
                    : System.Drawing.Color.FromArgb(13, 0, 0, 0);
                using var path = Capsule(0, 0, size, size, radius);
                using var brush = new System.Drawing.SolidBrush(color);
                g.FillPath(brush, path);
            }
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        });
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

    private static byte[] RenderIcon(string path, string query)
    {
        var size = GetInt(query, "s") == 18 ? 18 : 14;
        var kind = path switch
        {
            "/icon/deepseek" => ProviderKind.DeepSeek,
            "/icon/kimi" => ProviderKind.Kimi,
            _ => ProviderKind.Glm,
        };
        return BrandIcons.Png(kind, size);
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
