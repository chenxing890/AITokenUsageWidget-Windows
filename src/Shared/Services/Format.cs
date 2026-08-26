using System.Globalization;

namespace AITokenUsageWidget.Shared.Services;

/// <summary>文本格式化（与 macOS 版一致；时间相对化文案见 L10n）。</summary>
public static class Format
{
    /// <summary>大数中文格式化：55237346 → "5524万"；≥1 亿显示 "x.x亿"。</summary>
    public static string TokenCount(int value)
    {
        if (value >= 100_000_000)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}亿", value / 100_000_000.0);
        if (value >= 10_000)
            return string.Format(CultureInfo.InvariantCulture, "{0:F0}万", value / 10_000.0);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string Amount(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string Percent(double value) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>重置倒计时文本，如「2 小时后重置」；已过期返回「即将重置」。</summary>
    public static string ResetCountdown(DateTimeOffset? resetTime, DateTimeOffset now)
    {
        if (resetTime is not { } reset) return "";
        var remaining = reset - now;
        if (remaining.TotalSeconds <= 0) return L10n.Get("resetSoon");
        if (remaining.TotalMinutes < 1) return L10n.Get("resetSoon");
        if (remaining.TotalMinutes < 60)
            return L10n.Get("resetInMinutes", ((int)remaining.TotalMinutes).ToString());
        if (remaining.TotalHours < 24)
            return L10n.Get("resetInHours", ((int)Math.Floor(remaining.TotalHours)).ToString());
        return L10n.Get("resetInDays", ((int)Math.Floor(remaining.TotalDays)).ToString());
    }

    /// <summary>「x 分钟前」样式文本（>60 分钟回落到 HH:mm）。</summary>
    public static string TimeAgo(DateTimeOffset time, DateTimeOffset now)
    {
        var delta = now - time;
        if (delta.TotalSeconds < 60) return L10n.Get("justNow");
        if (delta.TotalMinutes < 60)
            return L10n.Get("minutesAgo", ((int)delta.TotalMinutes).ToString());
        return time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>本地时钟时间 HH:mm。</summary>
    public static string ClockTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
