using AITokenUsageWidget.Shared.Models;
using Microsoft.UI.Xaml;

namespace AITokenUsageWidget.App.Services;

/// <summary>
/// 主题联动（FR-3 / §4.4）：主窗口即时切换；写入共享配置后由调用方通知小组件刷新。
/// </summary>
public static class ThemeHelper
{
    private static FrameworkElement? _root;

    public static void Attach(FrameworkElement root) => _root = root;

    public static void Apply(ThemePreference theme)
    {
        if (_root == null) return;
        _root.RequestedTheme = theme.ForcedDark() switch
        {
            true => ElementTheme.Dark,
            false => ElementTheme.Light,
            _ => ElementTheme.Default, // 跟随系统
        };
    }
}
