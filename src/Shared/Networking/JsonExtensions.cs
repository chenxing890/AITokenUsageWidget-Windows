using System.Text.Json;

namespace AITokenUsageWidget.Shared.Networking;

/// <summary>
/// 弱类型 JSON 读取扩展：供应商接口字段名 / 类型不统一（数字可能是字符串、
/// 时间可能是 ISO 或 Epoch 毫秒），统一在此兜底（对齐 macOS JSONValue）。
/// </summary>
public static class JsonExtensions
{
    public static JsonElement? Property(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
            ? value
            : null;

    public static JsonElement? Property(this JsonElement? element, string name) =>
        element?.Property(name);

    public static JsonElement? Item(this JsonElement element, int index) =>
        element.ValueKind == JsonValueKind.Array
            ? (index >= 0 && index < element.GetArrayLength() ? element[index] : null)
            : null;

    public static JsonElement? Item(this JsonElement? element, int index) =>
        element?.Item(index);

    public static List<JsonElement>? ArrayItems(this JsonElement? element) =>
        element?.ValueKind == JsonValueKind.Array ? element.Value.EnumerateArray().ToList() : null;

    public static string? Str(this JsonElement? element) =>
        element?.ValueKind == JsonValueKind.String ? element.Value.GetString() : null;

    /// <summary>数字；字符串形如 "100" 也尝试转换（Kimi / DeepSeek 接口会返回字符串数字）。</summary>
    public static double? Dbl(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.Number => element.Value.TryGetDouble(out var d) ? d : null,
        JsonValueKind.String when double.TryParse(element.Value.GetString(), out var d) => d,
        _ => null,
    };

    public static int? Int(this JsonElement? element) => element.Dbl() is { } d
        ? d >= int.MinValue && d <= int.MaxValue ? (int)d : null
        : null;

    public static bool? Bool(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(element.Value.GetString(), out var b) => b,
        _ => null,
    };

    /// <summary>ISO 8601 日期（兼容任意精度小数秒，如 "2026-01-09T15:23:13.716839300Z"）。</summary>
    public static DateTimeOffset? IsoDate(this JsonElement? element)
    {
        var s = element.Str();
        if (s is null) return null;
        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            return date;
        return null;
    }

    /// <summary>Epoch 毫秒时间戳（GLM nextResetTime）。</summary>
    public static DateTimeOffset? EpochMsDate(this JsonElement? element) =>
        element.Dbl() is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms)
            : null;
}
