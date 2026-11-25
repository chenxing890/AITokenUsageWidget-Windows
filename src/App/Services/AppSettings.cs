using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Storage;

namespace AITokenUsageWidget.App.Services;

/// <summary>
/// 共享配置的应用侧门面（FR-3 / FR-5）：
/// 持有内存副本，所有修改即时持久化（自动保存），并通知小组件刷新。
/// </summary>
public static class AppSettings
{
    private static readonly ConfigStore Store = ConfigStore.Default;
    private static bool _initialized;
    private static bool _snapshotUnsafe; // 启动快照读取失败：落盘前必须重读成功

    public static SharedData Data { get; private set; } = SharedData.Empty();

    public static event Action? Changed;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Data = Store.LoadWithDefaults();
        _snapshotUnsafe = Store.LastLoadFailed;

        if (Store.LastQuarantinedFile is { } quarantined)
        {
            CorruptBackupNotice = $"配置文件已损坏并备份至 {quarantined}，请重新配置供应商。";
        }
    }

    /// <summary>
    /// 启动时读取失败（锁冲突/IO 抖动）时内存快照为空：任何写操作前先重试读取，
    /// 仍失败则只改内存、不落盘——绝不把空快照写回覆盖磁盘上的已配置内容。
    /// </summary>
    private static bool EnsureDiskSafeToWrite()
    {
        if (!_snapshotUnsafe) return true;
        var fresh = Store.Load();
        if (Store.LastLoadFailed) return false;
        Data = fresh;
        _snapshotUnsafe = false;
        return true;
    }

    /// <summary>配置文件损坏提示（一次性，主窗口 InfoBar 展示，§4.5）。</summary>
    public static string? CorruptBackupNotice { get; private set; }

    public static void ClearCorruptNotice() => CorruptBackupNotice = null;

    /// <summary>按 Id 查找实例（同 kind 多实例各有一份配置）；找不到返回 null。</summary>
    public static ProviderConfig? Config(Guid id) =>
        Data.Configs.FirstOrDefault(c => c.Id == id);

    // ---- 修改即保存（自动保存），并通知小组件 ----
    // 落盘前必须 EnsureDiskSafeToWrite：快照不可信时只改内存，绝不覆盖磁盘配置。

    /// <summary>按 Id 保存（不存在则追加）——同 kind 多实例互不覆盖。</summary>
    public static void SaveConfig(ProviderConfig config)
    {
        var safe = EnsureDiskSafeToWrite();
        var index = Data.Configs.FindIndex(c => c.Id == config.Id);
        if (index >= 0) Data.Configs[index] = config;
        else Data.Configs.Add(config);
        if (safe) Store.Save(Data);
        Commit();
    }

    /// <summary>新增实例（侧栏「+」菜单；同 kind 可多个）。</summary>
    public static void AddConfig(ProviderConfig config)
    {
        var safe = EnsureDiskSafeToWrite();
        Data.Configs.Add(config);
        if (safe) Store.Save(Data);
        Commit();
    }

    /// <summary>删除实例（右键删除，带确认）。</summary>
    public static void DeleteConfig(Guid id)
    {
        var safe = EnsureDiskSafeToWrite();
        Data.Configs.RemoveAll(c => c.Id == id);
        if (safe) Store.Save(Data);
        Commit();
    }

    public static void SetTheme(ThemePreference theme)
    {
        var safe = EnsureDiskSafeToWrite();
        Data.Theme = theme;
        if (safe) Store.SetTheme(theme);
        Commit();
    }

    public static void SetAlertsEnabled(bool enabled)
    {
        var safe = EnsureDiskSafeToWrite();
        Data.AlertsEnabled = enabled;
        if (safe) Store.SetAlertsEnabled(enabled);
        Commit();
    }

    /// <summary>修改阈值：清空已通知记录并立即重评估（FR-4）。</summary>
    public static void SetAlertThreshold(double threshold)
    {
        var safe = EnsureDiskSafeToWrite();
        Data.AlertThreshold = threshold;
        if (safe) Store.SetAlertThresholdAndReset(threshold);
        Commit();
    }

    /// <summary>保存后通知小组件立即重新拉取数据（命名事件，§5.2）。</summary>
    private static void Commit()
    {
        RefreshSignaler.Signal();
        Changed?.Invoke();
    }

    public static double[] AlertThresholdOptions => AlertEngine.ThresholdOptions;
}
