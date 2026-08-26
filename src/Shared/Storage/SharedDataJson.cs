using System.Text.Json;

namespace AITokenUsageWidget.Shared.Storage;

using Models;

/// <summary>
/// shared.json 的编解码。写入为标准结构；读取逐字段容错：
/// 任何单字段缺失 / 类型异常都不会导致整个文件解码失败（FR-5 防护第 1 层）。
/// 只有根级 JSON 无法解析时 TryParse 返回 false（调用方走 corrupt 保留流程）。
/// </summary>
public static class SharedDataJson
{
    public static string Write(SharedData data, ISecretProtector protector)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("configs");
            writer.WriteStartArray();
            foreach (var config in data.Configs) WriteConfig(writer, config, protector);
            writer.WriteEndArray();

            writer.WritePropertyName("cachedUsages");
            writer.WriteStartArray();
            foreach (var usage in data.CachedUsages) WriteUsage(writer, usage);
            writer.WriteEndArray();

            writer.WriteString("theme", data.Theme.KindId());
            writer.WritePropertyName("alertedKeys");
            writer.WriteStartArray();
            foreach (var key in data.AlertedKeys) writer.WriteStringValue(key);
            writer.WriteEndArray();
            writer.WriteBoolean("alertsEnabled", data.AlertsEnabled);
            writer.WriteNumber("alertThreshold", data.AlertThreshold);

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteConfig(Utf8JsonWriter writer, ProviderConfig config, ISecretProtector protector)
    {
        writer.WriteStartObject();
        writer.WriteString("id", config.Id);
        writer.WriteString("kind", config.Kind.KindId());
        writer.WriteBoolean("isEnabled", config.IsEnabled);
        writer.WriteString("apiKey", config.ApiKey.Length == 0 ? "" : protector.Protect(config.ApiKey));
        writer.WriteString("baseURLOverride", config.BaseURLOverride);
        writer.WriteString("customName", config.CustomName);
        writer.WriteString("extraToken", config.ExtraToken.Length == 0 ? "" : protector.Protect(config.ExtraToken));
        writer.WriteEndObject();
    }

    private static void WriteUsage(Utf8JsonWriter writer, ProviderUsage usage)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", usage.Kind.KindId());
        writer.WriteString("displayName", usage.DisplayName);
        writer.WriteString("state", usage.State switch
        {
            UsageState.Ok => "ok",
            UsageState.MissingKey => "missingKey",
            _ => "error",
        });
        if (usage.ErrorMessage != null) writer.WriteString("errorMessage", usage.ErrorMessage);
        if (usage.Currency != null) writer.WriteString("currency", usage.Currency);
        if (usage.TotalBalance is { } total) writer.WriteNumber("totalBalance", total);
        if (usage.GrantedBalance is { } granted) writer.WriteNumber("grantedBalance", granted);
        if (usage.ToppedUpBalance is { } toppedUp) writer.WriteNumber("toppedUpBalance", toppedUp);
        if (usage.IsAvailable is { } available) writer.WriteBoolean("isAvailable", available);
        writer.WritePropertyName("windows");
        writer.WriteStartArray();
        foreach (var window in usage.Windows) WriteWindow(writer, window);
        writer.WriteEndArray();
        writer.WriteString("fetchedAt", usage.FetchedAt.UtcDateTime.ToString("o"));
        writer.WriteBoolean("isSimulated", usage.IsSimulated);
        writer.WriteBoolean("fromCache", usage.FromCache);
        writer.WriteEndObject();
    }

    private static void WriteWindow(Utf8JsonWriter writer, UsageWindow window)
    {
        writer.WriteStartObject();
        writer.WriteString("title", window.Title);
        if (window.UsedPercent is { } percent) writer.WriteNumber("usedPercent", percent);
        if (window.UsedText != null) writer.WriteString("usedText", window.UsedText);
        if (window.ResetTime is { } reset) writer.WriteString("resetTime", reset.UtcDateTime.ToString("o"));
        writer.WriteEndObject();
    }

    /// <summary>容错解析。仅当根级不是合法 JSON 时返回 false（走 corrupt 保留）。</summary>
    public static bool TryParse(string json, ISecretProtector protector, out SharedData data)
    {
        data = new SharedData();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("configs", out var configsEl) && configsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in configsEl.EnumerateArray())
                {
                    var config = ReadConfig(element, protector);
                    if (config != null) data.Configs.Add(config);
                }
            }

            if (root.TryGetProperty("cachedUsages", out var usagesEl) && usagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in usagesEl.EnumerateArray())
                {
                    var usage = ReadUsage(element);
                    if (usage != null) data.CachedUsages.Add(usage);
                }
            }

            data.Theme = ReadString(root, "theme") is { } theme
                ? ThemePreferenceExtensions.FromKindId(theme)
                : ThemePreference.System;

            if (root.TryGetProperty("alertedKeys", out var keysEl) && keysEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in keysEl.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String && element.GetString() is { } key)
                        data.AlertedKeys.Add(key);
                }
            }

            data.AlertsEnabled = ReadBool(root, "alertsEnabled") ?? true;

            var threshold = ReadDouble(root, "alertThreshold");
            data.AlertThreshold = threshold is > 0 and <= 100 ? threshold.Value : 80;
        }
        return true;
    }

    private static ProviderConfig? ReadConfig(JsonElement element, ISecretProtector protector)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var kind = ProviderKindExtensions.FromKindId(ReadString(element, "kind"));
        if (kind is null) return null; // 未知供应商：跳过该条（不导致整文件失败）

        var config = new ProviderConfig(kind.Value)
        {
            // id 损坏时重新生成，保证配置仍可被编辑保存
            Id = Guid.TryParse(ReadString(element, "id"), out var id) ? id : Guid.NewGuid(),
            IsEnabled = ReadBool(element, "isEnabled") ?? false,
            ApiKey = protector.Unprotect(ReadString(element, "apiKey") ?? ""),
            // 兼容旧字段名 baseURL / baseUrl
            BaseURLOverride = ReadString(element, "baseURLOverride")
                ?? ReadString(element, "baseURL") ?? ReadString(element, "baseUrl") ?? "",
            CustomName = ReadString(element, "customName") ?? "",
            ExtraToken = protector.Unprotect(ReadString(element, "extraToken") ?? ""),
        };
        return config;
    }

    private static ProviderUsage? ReadUsage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var kind = ProviderKindExtensions.FromKindId(ReadString(element, "kind"));
        if (kind is null) return null;

        var usage = new ProviderUsage
        {
            Kind = kind.Value,
            DisplayName = ReadString(element, "displayName") ?? kind.Value.DisplayName(),
            State = ReadString(element, "state") switch
            {
                "missingKey" => UsageState.MissingKey,
                "error" => UsageState.Error,
                _ => UsageState.Ok,
            },
            ErrorMessage = ReadString(element, "errorMessage"),
            Currency = ReadString(element, "currency"),
            TotalBalance = ReadDouble(element, "totalBalance"),
            GrantedBalance = ReadDouble(element, "grantedBalance"),
            ToppedUpBalance = ReadDouble(element, "toppedUpBalance"),
            IsAvailable = ReadBool(element, "isAvailable"),
            Windows = [],
            FetchedAt = ReadDateTime(element, "fetchedAt") ?? DateTimeOffset.UtcNow,
            IsSimulated = ReadBool(element, "isSimulated") ?? false,
            FromCache = ReadBool(element, "fromCache") ?? false,
        };

        if (element.TryGetProperty("windows", out var windowsEl) && windowsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var windowEl in windowsEl.EnumerateArray())
            {
                if (windowEl.ValueKind != JsonValueKind.Object) continue;
                var title = ReadString(windowEl, "title");
                if (string.IsNullOrEmpty(title)) continue;
                usage.Windows.Add(new UsageWindow
                {
                    Title = title,
                    UsedPercent = ReadDouble(windowEl, "usedPercent"),
                    UsedText = ReadString(windowEl, "usedText"),
                    ResetTime = ReadDateTime(windowEl, "resetTime"),
                });
            }
        }
        return usage;
    }

    // ---- 容错字段读取 ----

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    /// <summary>数字或数字字符串（"88.50"）均可；其余情况返回 null 而非抛异常。</summary>
    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String when double.TryParse(el.GetString(), out var d) => d,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDateTime(JsonElement element, string name) =>
        ReadString(element, name) is { } s && DateTimeOffset.TryParse(s, out var time)
            ? time
            : null;
}
