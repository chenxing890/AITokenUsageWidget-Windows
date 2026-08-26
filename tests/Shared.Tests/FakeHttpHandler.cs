using System.Net;

namespace AITokenUsageWidget.Shared.Tests;

/// <summary>按 URL 子串路由到固定响应的假 HttpMessageHandler（golden 样本测试）。</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (int Status, string Content)> _routes = new();
    public List<(string Url, string? Auth)> Requests { get; } = new();

    public FakeHttpHandler Route(string urlContains, string content, int status = 200)
    {
        _routes[urlContains] = (status, content);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        Requests.Add((url, request.Headers.Authorization?.ToString()));
        var route = _routes.FirstOrDefault(kv => url.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
        if (route.Key == null)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"no route\"}"),
            });
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)route.Value.Status)
        {
            Content = new StringContent(route.Value.Content),
        });
    }
}
