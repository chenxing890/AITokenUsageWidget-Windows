namespace AITokenUsageWidget.Shared.Models;

/// <summary>界面主题偏好：主 App 窗口与小组件共用（FR-3 / §3.2）。</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public static class ThemePreferenceExtensions
{
    public static readonly ThemePreference[] All =
        [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    public static string KindId(this ThemePreference theme) => theme switch
    {
        ThemePreference.System => "system",
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => "system",
    };

    public static ThemePreference FromKindId(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemePreference.Light,
        "dark" => ThemePreference.Dark,
        _ => ThemePreference.System,
    };

    public static string Label(this ThemePreference theme) => theme switch
    {
        ThemePreference.System => "跟随系统",
        ThemePreference.Light => "浅色",
        ThemePreference.Dark => "深色",
        _ => "",
    };

    /// <summary>按偏好解析实际生效的明暗（null 表示跟随系统，由调用方读取系统外观）。</summary>
    public static bool? ForcedDark(this ThemePreference theme) => theme switch
    {
        ThemePreference.Light => false,
        ThemePreference.Dark => true,
        _ => null,
    };
}
