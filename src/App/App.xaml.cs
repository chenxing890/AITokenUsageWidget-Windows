using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Storage;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace AITokenUsageWidget.App;

/// <summary>
/// 主 App 入口：注册通知渠道、加载共享配置、应用主题、创建主窗口；
/// 处理协议激活（点击告警 Toast / 小组件引导拉起 → 定位对应供应商页）。
/// </summary>
public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }

    /// <summary>等待导航的供应商（Toast 点击带出），主窗口创建后消费。</summary>
    public static ProviderKind? PendingProviderNavigation { get; set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        L10n.UseSystemLanguage();
        AppSettings.Initialize();
        Services.ToastService.RegisterChannel(); // 首次启动注册通知渠道（FR-4）
        HandleActivation(AppInstance.GetCurrent().GetActivatedEventArgs());

        MainAppWindow = new MainWindow();
        ThemeHelper.Apply(AppSettings.Data.Theme);
        MainAppWindow.Activate();
    }

    private static void HandleActivation(AppActivationArguments activation)
    {
        if (activation.Kind != ExtendedActivationKind.Protocol) return;
        if (activation.Data is not IProtocolActivatedEventArgs protocol) return;

        // aitokenusagewidget://provider/{kind}
        var segments = protocol.Uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0] == "provider")
        {
            PendingProviderNavigation = ProviderKindExtensions.FromKindId(segments[1]);
        }
    }
}
