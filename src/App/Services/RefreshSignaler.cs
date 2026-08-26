namespace AITokenUsageWidget.App.Services;

/// <summary>
/// 主 App → Widget Provider 的命名事件通知（§5.2 刷新调度）：
/// 配置保存后 set 命名 EventWaitHandle，Provider 端监听并立即刷新。
/// </summary>
public static class RefreshSignaler
{
    public const string EventName = @"Local\AITokenUsageWidget.RefreshSignal";

    private static EventWaitHandle? _event;

    private static EventWaitHandle GetOrCreate()
    {
        return _event ??= new EventWaitHandle(
            false, EventResetMode.AutoReset, EventName);
    }

    public static void Signal()
    {
        try
        {
            GetOrCreate().Set();
        }
        catch (PlatformNotSupportedException)
        {
            // 非 Windows 环境（单测）无命名事件
        }
    }
}
