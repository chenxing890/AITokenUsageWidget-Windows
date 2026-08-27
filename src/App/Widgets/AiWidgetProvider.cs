using System.Runtime.InteropServices;
using AITokenUsageWidget.Shared.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// IWidgetProvider 实现（对齐微软官方示例的生命周期）：
/// CreateWidget / DeleteWidget / Activate / Deactivate / OnActionInvoked / OnWidgetContextChanged。
/// </summary>
[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("8B4D2E11-6B7C-4F2A-9B0D-3C1E5A9F7D42")]
public sealed class AiWidgetProvider : IWidgetProvider
{
    public const string DefinitionId = "AITokenUsageWidget.Main";
    private const string RefreshVerb = "refresh";
    private const string OpenAppVerb = "openApp";

    private static bool _recovered;

    public AiWidgetProvider()
    {
        RecoverRunningWidgets();
    }

    /// <summary>Provider 重启后恢复已有小组件实例（板面持有的 widget 仍指向本 Provider）。</summary>
    private static void RecoverRunningWidgets()
    {
        if (_recovered) return;
        _recovered = true;
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var context = info.WidgetContext;
                if (context.DefinitionId != DefinitionId) continue;
                if (!WidgetRefreshService.Instances.ContainsKey(context.Id))
                {
                    WidgetRefreshService.RegisterWidget(context.Id, ToSize(context.Size));
                }
            }
        }
        catch (Exception)
        {
            // 板面不可用时忽略；激活路径会重新注册
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        WidgetRefreshService.RegisterWidget(widgetContext.Id, ToSize(widgetContext.Size));
        // 立即用缓存数据上屏，随后 RefreshAll 拉取最新
        var data = Shared.Storage.ConfigStore.Default.Load();
        WidgetRefreshService.UpdateWidget(
            widgetContext.Id, ToSize(widgetContext.Size), data.CachedUsages, data.Theme);
        WidgetRefreshService.RefreshAll("create:" + widgetContext.Id);
    }

    public void DeleteWidget(string widgetId, string _)
    {
        WidgetRefreshService.UnregisterWidget(widgetId);
    }

    public void Activate(WidgetContext widgetContext)
    {
        // 板面开始展示：注册并立即刷新（§5.2 Provider 激活时立即刷新一次）
        WidgetRefreshService.RegisterWidget(widgetContext.Id, ToSize(widgetContext.Size));
        WidgetRefreshService.RefreshAll("activate:" + widgetContext.Id);
    }

    public void Deactivate(string widgetId)
    {
        // 板面隐藏：暂停推屏（刷新定时器继续，数据/告警不受影响）
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (actionInvokedArgs.Verb == RefreshVerb)
        {
            WidgetRefreshService.RefreshNow(actionInvokedArgs.WidgetContext.Id);
        }
        else if (actionInvokedArgs.Verb == OpenAppVerb)
        {
            LaunchMainApp();
        }
    }

    /// <summary>通过已注册协议拉起主 App（Board 会拦截卡片内 OpenUrl 的自定义协议）。</summary>
    private static void LaunchMainApp()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "aitokenusagewidget:",
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 主 App 未安装/协议缺失时静默忽略
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        WidgetRefreshService.RegisterWidget(context.Id, ToSize(context.Size));
        WidgetRefreshService.RefreshAll("resize:" + context.Id);
    }

    private static WidgetSize ToSize(Microsoft.Windows.Widgets.WidgetSize size) => size switch
    {
        Microsoft.Windows.Widgets.WidgetSize.Large => WidgetSize.Large,
        _ => WidgetSize.Medium,
    };
}
