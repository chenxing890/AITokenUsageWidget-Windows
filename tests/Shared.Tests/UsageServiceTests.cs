using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Networking;
using AITokenUsageWidget.Shared.Services;
using Xunit;

namespace AITokenUsageWidget.Shared.Tests;

public class UsageServiceTests
{
    private static UsageService Service(FakeHttpHandler handler) => new(new ApiHttp(handler));

    // ---- DeepSeek ----

    [Fact]
    public async Task DeepSeek_ParsesBalance()
    {
        var handler = new FakeHttpHandler().Route("/user/balance",
            """
            {"is_available":true,"balance_infos":[
              {"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"}
            ]}
            """);
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.DeepSeek, "sk-ds"));

        Assert.Equal(UsageState.Ok, usage.State);
        Assert.Equal("CNY", usage.Currency);
        Assert.Equal(110.00, usage.TotalBalance);
        Assert.Equal(10.00, usage.GrantedBalance);
        Assert.Equal(100.00, usage.ToppedUpBalance);
        Assert.True(usage.IsAvailable);
        Assert.Equal("¥", usage.CurrencySymbol);
    }

    [Fact]
    public async Task DeepSeek_EmptyBalanceInfo_IsError()
    {
        var handler = new FakeHttpHandler().Route("/user/balance", """{"is_available":false}""");
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.DeepSeek, "sk-x"));
        Assert.Equal(UsageState.Error, usage.State);
        Assert.Equal("服务器返回为空", usage.ErrorMessage);
    }

    // ---- Kimi ----

    [Fact]
    public async Task Kimi_ParsesWindowsAndFallsBackUsedFromRemaining()
    {
        var handler = new FakeHttpHandler().Route("/usages",
            """
            {
              "limits": [
                {"window": {"duration": 300},
                 "detail": {"limit": "100", "remaining": "58", "resetTime": "2026-01-09T15:23:13.716839300Z"}},
                {"window": {"duration": 10080},
                 "detail": {"limit": "999", "remaining": "999", "resetTime": "2026-01-12T00:00:00Z"}}
              ],
              "usage": {"limit": "500", "used": "340", "remaining": "160", "resetTime": "2026-01-12T00:00:00Z"}
            }
            """);
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Kimi, "sk-kimi"));

        Assert.Equal(UsageState.Ok, usage.State);
        Assert.Equal(2, usage.Windows.Count); // 5 小时（limits 内）+ 7 天（顶层 usage）

        var five = usage.Windows[0];
        Assert.Equal("5 小时", five.Title);
        Assert.Equal(42, five.UsedPercent); // used 缺失：limit - remaining = 100 - 58
        Assert.Equal("2026-01-09T15:23:13.7168393Z", five.ResetTime?.UtcDateTime.ToString("O"));

        Assert.Equal(68, usage.Windows[1].UsedPercent); // 顶层 usage → 7 天
        Assert.Equal("7 天", usage.Windows[1].Title);
    }

    [Fact]
    public async Task Kimi_MonthlyOptionalAndSilentlyIgnored()
    {
        var handler = new FakeHttpHandler()
            .Route("/usages", """{"usage": {"limit": "500", "used": "100", "resetTime": "2026-01-12T00:00:00Z"}}""")
            .Route("kimi.gateway.billing", "{\"totalQuota\":{\"limit\":\"1000\",\"used\":\"350\"}}");
        var config = Config(ProviderKind.Kimi, "sk-kimi");
        config.ExtraToken = "kimi-auth-cookie";
        var usage = await Service(handler).FetchAsync(config);

        Assert.Equal(2, usage.Windows.Count);
        Assert.Equal("本月总额", usage.Windows[1].Title);
        Assert.Equal(35, usage.Windows[1].UsedPercent);
    }

    [Fact]
    public async Task Kimi_MonthlyFailure_DoesNotAffectOtherWindows()
    {
        var handler = new FakeHttpHandler()
            .Route("/usages", """{"usage": {"limit": "500", "used": "100", "resetTime": "2026-01-12T00:00:00Z"}}""")
            .Route("kimi.gateway.billing", "gone", status: 500);
        var config = Config(ProviderKind.Kimi, "sk-kimi");
        config.ExtraToken = "expired-cookie";

        var usage = await Service(handler).FetchAsync(config);
        Assert.Equal(UsageState.Ok, usage.State);
        Assert.Single(usage.Windows);
    }

    // ---- GLM ----

    private const string GlmLimitsJson =
        """
        {"success":true,"code":200,"data":{"limits":[
          {"type":"TOKENS_LIMIT","unit":3,"percentage":21.5,"nextResetTime":1767225600000},
          {"type":"TOKENS_LIMIT","unit":6,"percentage":55.0,"nextResetTime":1767830400000},
          {"type":"TIME_LIMIT","currentValue":126,"usage":1000,"percentage":12.0,"nextResetTime":1769385600000}
        ]}}
        """;

    private const string GlmModelUsageJson =
        """{"success":true,"data":{"totalUsage":{"totalTokensUsage":55237346,"totalModelCallCount":1234}}}""";

    [Fact]
    public async Task Glm_ParsesQuotaToolsAndCumulative()
    {
        var handler = new FakeHttpHandler()
            .Route("quota/limit", GlmLimitsJson)
            .Route("model-usage", GlmModelUsageJson);
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Glm, "glm-key"));

        Assert.Equal(UsageState.Ok, usage.State);
        Assert.Equal(4, usage.Windows.Count);

        Assert.Equal("5 小时", usage.Windows[0].Title);
        Assert.Equal(21.5, usage.Windows[0].UsedPercent);
        Assert.Equal("7 天", usage.Windows[1].Title);
        Assert.Equal(55, usage.Windows[1].UsedPercent);
        Assert.Equal("月度工具", usage.Windows[2].Title);
        Assert.Equal("126/1000 次", usage.Windows[2].UsedText);

        var cumulative = usage.Windows[3];
        Assert.Equal("30 天累计", cumulative.Title);
        Assert.Null(cumulative.UsedPercent);
        Assert.Equal("5524万 tokens · 1234 次", cumulative.UsedText); // 55237346 → 5524万

        // Authorization 无 Bearer 前缀
        var quotaRequest = handler.Requests.First(r => r.Url.Contains("quota/limit"));
        Assert.Equal("glm-key", quotaRequest.Auth);
    }

    [Fact]
    public async Task Glm_MissingUnit_AssignsByResetOrder()
    {
        var json =
            """
            {"success":true,"data":{"limits":[
              {"type":"CREDIT_LIMIT","nextResetTime":1767830400000,"currentValue":80,"usage":200},
              {"type":"CREDIT_LIMIT","nextResetTime":1767225600000,"currentValue":40,"usage":200}
            ]}}
            """;
        var handler = new FakeHttpHandler()
            .Route("quota/limit", json)
            .Route("model-usage", "{}");
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Glm, "glm-key"));

        // 先重置的为 5 小时，其次 7 天；CREDIT_LIMIT 由次数计算百分比与文本
        Assert.Equal("5 小时", usage.Windows[0].Title);
        Assert.Equal(20, usage.Windows[0].UsedPercent);
        Assert.Equal("40/200", usage.Windows[0].UsedText);
        Assert.Equal("7 天", usage.Windows[1].Title);
        Assert.Equal("80/200", usage.Windows[1].UsedText);
    }

    [Fact]
    public async Task Glm_Http200BusinessFailure_SurfacesMessage()
    {
        var handler = new FakeHttpHandler()
            .Route("quota/limit", """{"success":false,"code":401,"msg":"token 无效"}""")
            .Route("model-usage", "{}");
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Glm, "bad"));

        Assert.Equal(UsageState.Error, usage.State);
        Assert.Equal("token 无效", usage.ErrorMessage);
    }

    [Fact]
    public async Task Glm_CumulativeFailure_IsSilentlyIgnored()
    {
        var handler = new FakeHttpHandler()
            .Route("quota/limit", GlmLimitsJson)
            .Route("model-usage", "not-json");
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Glm, "glm-key"));

        Assert.Equal(UsageState.Ok, usage.State);
        Assert.Equal(3, usage.Windows.Count); // 无 30 天累计
    }

    // ---- 通用 ----

    [Fact]
    public async Task MissingKey_ReturnsMissingKeyState()
    {
        var usage = await new UsageService().FetchAsync(new ProviderConfig(ProviderKind.Kimi));
        Assert.Equal(UsageState.MissingKey, usage.State);
        Assert.Empty(usage.Windows);
    }

    [Fact]
    public async Task Http401_MapsToFriendlyAuthError()
    {
        var handler = new FakeHttpHandler().Route("/user/balance", "unauthorized", status: 401);
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.DeepSeek, "sk-bad"));

        Assert.Equal(UsageState.Error, usage.State);
        Assert.Equal("认证失败（HTTP 401）：请检查 API Key 是否正确", usage.ErrorMessage);
    }

    [Fact]
    public async Task Http429_MapsToRateLimit()
    {
        var handler = new FakeHttpHandler().Route("/usages", "slow down", status: 429);
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Kimi, "sk-kimi"));
        Assert.Equal("请求过于频繁（HTTP 429），请稍后重试", usage.ErrorMessage);
    }

    [Fact]
    public async Task InvalidJson_MapsToParseError()
    {
        var handler = new FakeHttpHandler().Route("/usages", "<html>gateway</html>");
        var usage = await Service(handler).FetchAsync(Config(ProviderKind.Kimi, "sk-kimi"));
        Assert.Equal("响应解析失败，接口格式可能已变更", usage.ErrorMessage);
    }

    [Fact]
    public async Task FetchAll_PreservesOrderAndCleansBearerPrefix()
    {
        var handler = new FakeHttpHandler()
            .Route("deepseek", """{"balance_infos":[{"currency":"USD","total_balance":"5"}]}""")
            .Route("kimi", """{"usage": {"limit": "10", "used": "5", "resetTime": "2026-01-12T00:00:00Z"}}""");

        var configs = new List<ProviderConfig>
        {
            Config(ProviderKind.Kimi, "Bearer sk-kimi"), // 误粘贴 Bearer 前缀
            Config(ProviderKind.DeepSeek, "sk-ds"),
        };
        var usages = await Service(handler).FetchAllAsync(configs);

        Assert.Equal(2, usages.Count);
        Assert.Equal(ProviderKind.Kimi, usages[0].Kind); // 顺序保持
        Assert.Equal(ProviderKind.DeepSeek, usages[1].Kind);
        Assert.Equal("$", usages[1].CurrencySymbol);
        Assert.Equal(5, usages[1].TotalBalance);
    }

    [Fact]
    public void TokenCount_ChineseBigNumberFormat()
    {
        Assert.Equal("5524万", Format.TokenCount(55_237_346));
        Assert.Equal("1.2亿", Format.TokenCount(120_000_000));
        Assert.Equal("9999", Format.TokenCount(9999));
    }

    internal static ProviderConfig Config(ProviderKind kind, string apiKey)
    {
        var config = new ProviderConfig(kind) { ApiKey = apiKey };
        return config;
    }
}
