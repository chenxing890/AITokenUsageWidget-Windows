using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AITokenUsageWidget.Shared.Networking;

/// <summary>携带用户可读中文消息的 API 异常（对齐 macOS APIError）。</summary>
public sealed class ApiException : Exception
{
    public int? StatusCode { get; }

    public ApiException(string userMessage, int? statusCode = null) : base(userMessage)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// 极简 HTTP 客户端（App 与 Widget Provider 共用）。
/// 单例 HttpClient、10 秒超时（§6.1）；错误统一转中文友好提示。
/// 构造函数可注入 HttpMessageHandler 以便单元测试。
/// </summary>
public sealed class ApiHttp
{
    public const int TimeoutSeconds = 10;

    private readonly HttpClient _client;

    public ApiHttp(HttpMessageHandler? handler = null)
    {
        _client = handler == null
            ? new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                AutomaticDecompression = DecompressionMethods.All,
                // HttpClient.Timeout 在某些环境下无法打断底层 TCP 连接阶段，
                // ConnectTimeout 兜底，保证连接阶段最坏 10 秒返回
                ConnectTimeout = TimeSpan.FromSeconds(TimeoutSeconds),
            })
            : new HttpClient(handler, disposeHandler: false);
        _client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("AITokenUsageWidget-Windows/1.0");
    }

    public Task<JsonElement> GetJsonAsync(string url, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(url, HttpMethod.Get, headers, body: null);

    public Task<JsonElement> PostJsonAsync(string url, IReadOnlyDictionary<string, string>? headers, string? body) =>
        SendAsync(url, HttpMethod.Post, headers, body);

    private async Task<JsonElement> SendAsync(string url, HttpMethod method,
        IReadOnlyDictionary<string, string>? headers, string? body)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ApiException("接口地址无效");

        using var request = new HttpRequestMessage(method, uri);
        if (headers != null)
        {
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        }
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        byte[] data;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            data = await response.Content.ReadAsByteArrayAsync();
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("请求超时，请检查网络");
        }
        catch (HttpRequestException e)
        {
            throw new ApiException(FriendlyMessageFor(e));
        }
        catch (HttpIOException)
        {
            throw new ApiException("网络连接失败，请检查网络");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw (int)response.StatusCode switch
                {
                    401 or 403 => new ApiException(
                        $"认证失败（HTTP {(int)response.StatusCode}）：请检查 API Key 是否正确", (int)response.StatusCode),
                    429 => new ApiException("请求过于频繁（HTTP 429），请稍后重试", 429),
                    _ => new ApiException($"请求失败（HTTP {(int)response.StatusCode}）", (int)response.StatusCode),
                };
            }

            if (data.Length == 0)
                throw new ApiException("服务器返回为空");

            try
            {
                using var document = JsonDocument.Parse(data);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new ApiException("响应解析失败，接口格式可能已变更");
            }
        }
    }

    private static string FriendlyMessageFor(HttpRequestException e) => e.InnerException switch
    {
        TimeoutException => "请求超时，请检查网络",
        SocketException => "网络连接失败，请检查网络",
        _ => "网络连接失败，请检查网络",
    };

    /// <summary>把底层错误转成用户可读的中文提示（对齐 macOS friendlyMessage）。</summary>
    public static string FriendlyMessage(Exception error) => error switch
    {
        ApiException api => api.Message,
        TaskCanceledException => "请求超时，请检查网络",
        HttpRequestException or SocketException => "网络连接失败，请检查网络",
        JsonException => "响应解析失败，接口格式可能已变更",
        _ => $"请求失败：{error.Message}",
    };
}
