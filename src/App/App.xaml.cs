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
        // 单实例（FR-5 前置）：小组件「打开应用」/Toast 每次协议激活都会拉起新进程，
        // 多实例并发持有不同的配置快照，后写者会把先写者的 API Key 覆盖掉——
        // 已注册实例直接重定向激活并退出本进程
        var main = AppInstance.FindOrRegisterForKey("AITokenUsageWidget.Main");
        if (!main.IsCurrent)
        {
            main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
            Exit();
            return;
        }
        // 主实例：接收后续激活（唤起已有窗口，而不是再开一个）
        main.Activated += (_, _) =>
        {
            if (MainAppWindow == null) return;
            MainAppWindow.DispatcherQueue.TryEnqueue(() => MainAppWindow.Activate());
        };

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
