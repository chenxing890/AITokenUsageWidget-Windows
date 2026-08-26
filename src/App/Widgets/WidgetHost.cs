namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// COM 服务器宿主（无头模式，Program.Main 以 -RegisterProcessAsComServer 进入）：
/// 注册 Widget Provider 类对象 → 启动刷新引擎 → STA 消息泵直到无小组件存活。
/// </summary>
public static class WidgetHost
{
    public static void RunComServer()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        uint cookie = 0;
        try
        {
            cookie = WidgetComInterop.RegisterClassObject(
                typeof(AiWidgetProvider).GUID,
                new WidgetComInterop.WidgetProviderFactory(() => new AiWidgetProvider()));

            WidgetRefreshService.Start(WidgetComInterop.GetCurrentThreadId());
            WidgetComInterop.RunMessagePump();
        }
        catch (Exception)
        {
            // 记录后静默退出，板面会在下次需要时重新拉起
        }
        finally
        {
            WidgetRefreshService.Stop();
            WidgetComInterop.RevokeClassObject(cookie);
        }
    }
}
