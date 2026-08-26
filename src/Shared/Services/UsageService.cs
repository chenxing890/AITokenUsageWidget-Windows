using System.Globalization;
using System.Text.Json;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Networking;

namespace AITokenUsageWidget.Shared.Services;

/// <summary>
/// 三家供应商的用量抓取与解析（与 macOS v1.0 逐条对齐，FR-1）。
/// 查询接口不消耗模型 token；Key 未填返回 MissingKey；失败返回 Error（中文提示）。
/// 新增供应商：① ProviderKind 增加枚举 ② 此处增加分支 ③ UI/存储/卡片自动适配。
/// </summary>
public sealed class UsageService
{
    /// <summary>Kimi 月度总额（非官方网页接口，可能失效，失败静默忽略）。</summary>
    public const string KimiBillingURL = "https://www.kimi.com/apiv2/kimi.gateway.billing.v1.BillingService/GetUsages";

    private readonly ApiHttp _http;

    public UsageService(ApiHttp? http = null) => _http = http ?? new ApiHttp();

    /// <summary>并发抓取全部供应商（保持传入顺序）。</summary>
    public async Task<List<ProviderUsage>> FetchAllAsync(IEnumerable<ProviderConfig> configs)
    {
        var list = configs.ToList();
        var results = await Task.WhenAll(list.Select(FetchAsync));
        return results.ToList();
    }

    /// <summary>抓取单个供应商。</summary>
    public async Task<ProviderUsage> FetchAsync(ProviderConfig config)
    {
        if (!config.HasKey)
        {
            return Usage(UsageState.MissingKey, config);
        }
        try
        {
            return config.Kind switch
            {
                ProviderKind.DeepSeek => await FetchDeepSeekAsync(config),
                ProviderKind.Kimi => await FetchKimiAsync(config),
                ProviderKind.Glm => await FetchGlmAsync(config),
                _ => Usage(UsageState.Error, config, "未知供应商"),
            };
        }
        catch (Exception e)
        {
            return Usage(UsageState.Error, config, ApiHttp.FriendlyMessage(e));
        }
    }

    private static ProviderUsage Usage(UsageState state, ProviderConfig config, string? message = null) => new()
    {
        Kind = config.Kind,
        DisplayName = config.Name,
        State = state,
        ErrorMessage = state == UsageState.Error ? message ?? "请求失败" : null,
        Windows = [],
        FetchedAt = DateTimeOffset.UtcNow,
    };

    // ---- DeepSeek（账户余额） ----

    /// <summary>GET /user/balance —— 总余额 / 赠送 / 充值 + 可用状态；空响应视为错误。</summary>
    private async Task<ProviderUsage> FetchDeepSeekAsync(ProviderConfig config)
    {
        var json = await _http.GetJsonAsync(
            config.BuildApiURL("/user/balance"),
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {config.CleanAPIKey}" });

        var info = json.Property("balance_infos").Item(0);
        if (info.Property("total_balance").Dbl() is not { } total)
            throw new ApiException("服务器返回为空");

        return new ProviderUsage
        {
            Kind = ProviderKind.DeepSeek,
            DisplayName = config.Name,
            State = UsageState.Ok,
            Currency = info.Property("currency").Str(),
            TotalBalance = total,
            GrantedBalance = info.Property("granted_balance").Dbl(),
            ToppedUpBalance = info.Property("topped_up_balance").Dbl(),
            IsAvailable = json.Property("is_available").Bool(),
            Windows = [],
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }

    // ---- Kimi Code（5 小时 / 7 天 / 月度总额） ----

    /// <summary>
    /// GET /usages —— limits[].window.duration == 300（分钟）为 5 小时窗口（数据在 detail 内），
    /// 顶层 usage 为 7 天窗口；月度走非官方 billing 接口（可选，失败静默忽略）。
    /// </summary>
    private async Task<ProviderUsage> FetchKimiAsync(ProviderConfig config)
    {
        var json = await _http.GetJsonAsync(
            config.BuildApiURL("/usages"),
            new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {config.CleanAPIKey}",
                ["Accept"] = "application/json",
            });

        var windows = new List<UsageWindow>();
        // 5 小时窗口
        foreach (var limit in json.Property("limits").ArrayItems() ?? [])
        {
            if (limit.Property("window").Property("duration").Dbl() == 300)
            {
                var window = MakeKimiWindow("5 小时", limit.Property("detail"));
                if (window != null) windows.Add(window);
            }
        }
        // 7 天窗口：顶层 usage
        if (MakeKimiWindow("7 天", json.Property("usage")) is { } weekly)
            windows.Add(weekly);
        // 月度总额（可选）
        if (config.HasExtraToken && await FetchKimiMonthlyAsync(config.ExtraToken.Trim()) is { } monthly)
            windows.Add(monthly);

        if (windows.Count == 0)
            throw new ApiException("服务器返回为空");

        return new ProviderUsage
        {
            Kind = ProviderKind.Kimi,
            DisplayName = config.Name,
            State = UsageState.Ok,
            Windows = windows,
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>POST kimi.com 网页 billing 接口（kimi-auth Cookie → Bearer），totalQuota 为月度总额。</summary>
    private async Task<UsageWindow?> FetchKimiMonthlyAsync(string cookie)
    {
        try
        {
            var json = await _http.PostJsonAsync(
                KimiBillingURL,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {cookie}",
                    ["Content-Type"] = "application/json",
                },
                "{}");
            return MakeKimiWindow("本月总额", json.Property("totalQuota"));
        }
        catch (ApiException)
        {
            return null; // 静默忽略，不影响前两个窗口
        }
    }

    /// <summary>Kimi 窗口结构 { limit, used, remaining, resetTime }，数字以字符串返回；
    /// used 缺失时用 limit - remaining 计算。</summary>
    private static UsageWindow? MakeKimiWindow(string title, JsonElement? detail)
    {
        if (detail is null) return null;
        var limit = detail.Value.Property("limit").Dbl();
        var used = detail.Value.Property("used").Dbl()
            ?? limit - (detail.Value.Property("remaining").Dbl() ?? 0);
        double? percent = used is { } u && limit is { } l && l > 0
            ? Math.Min(u / l * 100, 100)
            : null;
        return new UsageWindow
        {
            Title = title,
            UsedPercent = percent,
            UsedText = null,
            ResetTime = detail.Value.Property("resetTime").IsoDate(),
        };
    }

    // ---- GLM Coding（5 小时 / 7 天 / 月度工具 / 30 天累计） ----

    /// <summary>
    /// GET /api/monitor/usage/quota/limit。HTTP 200 也可能业务失败，必须检查
    /// success == false / error / code != 200 并透出 msg。TOKENS_LIMIT（老套餐）仅百分比，
    /// CREDIT_LIMIT（新套餐）返回次数绝对值；unit 3 = 5 小时、6 = 7 天，缺 unit 按重置时间排序。
    /// </summary>
    private async Task<ProviderUsage> FetchGlmAsync(ProviderConfig config)
    {
        var key = config.CleanAPIKey;
        var json = await _http.GetJsonAsync(
            config.BuildApiURL("/api/monitor/usage/quota/limit"),
            new Dictionary<string, string>
            {
                ["Authorization"] = key, // 无 Bearer 前缀
                ["Accept"] = "application/json",
            });

        // 业务失败检查（GLM 鉴权 / 配额错误仍返回 HTTP 200）
        if (json.Property("success").Bool() == false
            || json.Property("error") != null
            || json.Property("code").Int() is { } code && code != 200)
        {
            var message = json.Property("msg").Str()
                ?? json.Property("error").Property("message").Str()
                ?? "接口返回错误";
            throw new ApiException(message);
        }

        var limits = json.Property("data").Property("limits").ArrayItems() ?? [];
        if (limits.Count == 0)
            throw new ApiException("服务器返回为空");

        var windows = new List<UsageWindow>();

        // 额度窗口：TOKENS_LIMIT / CREDIT_LIMIT；缺 unit 时按 nextResetTime 升序（先重置的为 5 小时）
        var quotaLimits = limits
            .Where(l => l.Property("type").Str() is "TOKENS_LIMIT" or "CREDIT_LIMIT")
            .OrderBy(l => l.Property("nextResetTime").Dbl() ?? 0)
            .ToList();
        var fiveHourSlotUsed = false;
        foreach (var limit in quotaLimits)
        {
            var title = limit.Property("unit").Int() switch
            {
                3 => "5 小时",
                6 => "7 天",
                _ => fiveHourSlotUsed ? "7 天" : "5 小时", // 老套餐缺 unit 字段
            };
            if (title == "5 小时") fiveHourSlotUsed = true;

            var used = limit.Property("currentValue").Int();
            var total = limit.Property("usage").Int();
            var percent = limit.Property("percentage").Dbl();
            if (percent is null && used is { } u && total is { } t && t > 0)
                percent = Math.Min((double)u / t * 100, 100);
            string? text = used is { } usedValue && total is { } totalValue
                ? $"{usedValue}/{totalValue}"
                : null;

            windows.Add(new UsageWindow
            {
                Title = title,
                UsedPercent = percent,
                UsedText = text,
                ResetTime = limit.Property("nextResetTime").EpochMsDate(),
            });
        }

        // 月度工具窗口（TIME_LIMIT）：次数绝对值
        var monthly = limits.FirstOrDefault(l => l.Property("type").Str() == "TIME_LIMIT");
        if (monthly.ValueKind != JsonValueKind.Undefined)
        {
            var used = monthly.Property("currentValue").Int();
            var total = monthly.Property("usage").Int();
            windows.Add(new UsageWindow
            {
                Title = "月度工具",
                UsedPercent = monthly.Property("percentage").Dbl(),
                UsedText = used != null || total != null ? $"{used ?? 0}/{total ?? 0} 次" : null,
                ResetTime = monthly.Property("nextResetTime").EpochMsDate(),
            });
        }

        // 30 天累计消耗（model-usage 统计接口，免 Cookie，失败静默忽略）
        if (await FetchGlmCumulativeAsync(config, key) is { } cumulative)
            windows.Add(cumulative);

        if (windows.Count == 0)
            throw new ApiException("服务器返回为空");

        return new ProviderUsage
        {
            Kind = ProviderKind.Glm,
            DisplayName = config.Name,
            State = UsageState.Ok,
            Windows = windows,
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>GET /api/monitor/usage/model-usage?startTime&amp;endTime（yyyy-MM-dd HH:mm:ss，近 30 天），
    /// 返回 totalUsage.totalTokensUsage / totalModelCallCount，失败静默忽略。</summary>
    private async Task<UsageWindow?> FetchGlmCumulativeAsync(ProviderConfig config, string key)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var format = "yyyy-MM-dd HH:mm:ss";
            var url = config.BuildApiURL("/api/monitor/usage/model-usage")
                + "?startTime=" + Uri.EscapeDataString(now.AddDays(-30).ToString(format))
                + "&endTime=" + Uri.EscapeDataString(now.ToString(format));
            var json = await _http.GetJsonAsync(url, new Dictionary<string, string>
            {
                ["Authorization"] = key,
                ["Accept"] = "application/json",
            });
            var total = json.Property("data").Property("totalUsage");
            var tokens = total.Property("totalTokensUsage").Int() ?? 0;
            var calls = total.Property("totalModelCallCount").Int() ?? 0;
            if (tokens <= 0 && calls <= 0) return null;
            return new UsageWindow
            {
                Title = "30 天累计",
                UsedPercent = null,
                UsedText = $"{Format.TokenCount(tokens)} tokens · {calls} 次",
                ResetTime = null,
            };
        }
        catch (ApiException)
        {
            return null; // 静默忽略，不影响主窗口
        }
    }
}
