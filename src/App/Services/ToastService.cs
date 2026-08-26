using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using Microsoft.Windows.AppNotifications;

namespace AITokenUsageWidget.App.Services;

/// <summary>
/// 用量告警 Toast（FR-4）：标题「{供应商} 用量告警」，正文「{窗口}已使用 N%，达到告警阈值 M%」；
/// 点击通过协议 aitokenusagewidget://provider/{kind} 打开主 App 对应供应商页。
/// </summary>
public static class ToastService
{
    /// <summary>首次启动注册通知渠道。</summary>
    public static void RegisterChannel()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => { };
            AppNotificationManager.Default.Register();
        }
        catch (Exception)
        {
            // 通知注册失败不阻断启动
        }
    }

    public static void Show(UsageAlert alert)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(alert.Title, new AppNotificationTextProperties().SetMaxLines(1))
                .AddText(alert.Body)
                .SetInvokeUri(new Uri(
                    $"aitokenusagewidget://provider/{alert.Kind.KindId()}"));
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
            // Toast 失败不影响刷新流程
        }
    }
}
