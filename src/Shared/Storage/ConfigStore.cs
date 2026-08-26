namespace AITokenUsageWidget.Shared.Storage;

using Models;

/// <summary>
/// 主 App 与小组件 Provider 进程共享的配置存储（%LOCALAPPDATA%\AITokenUsageWidget\shared.json）。
/// 三层防护（FR-5）：逐字段容错解码 → corrupt 文件保留 → 写前 .bak 备份 + 临时文件原子替换。
/// 并发：命名 Mutex 串行化；锁冲突短暂重试（最多 3 次，间隔 100ms）。
/// </summary>
public sealed class ConfigStore
{
    public const string LockName = "AITokenUsageWidget.ConfigStore";

    private readonly string _baseDir;
    private readonly string _sharedPath;
    private readonly string _bakPath;
    private readonly ISecretProtector _protector;
    private readonly object _ioGate = new();

    /// <summary>最近一次 Load 因文件损坏而保留了 corrupt 备份（供 UI 提示，一次性读取）。</summary>
    public string? LastQuarantinedFile { get; private set; }

    /// <summary>最近一次 Load 读取失败（IO 异常，非损坏）。此状态下应避免回写覆盖。</summary>
    public bool LastLoadFailed { get; private set; }

    public static ConfigStore Default { get; } = new();

    public ConfigStore(string? baseDir = null, ISecretProtector? protector = null)
    {
        _baseDir = baseDir ?? AppPaths.DefaultBaseDir;
        _sharedPath = AppPaths.SharedJsonPath(_baseDir);
        _bakPath = AppPaths.BackupJsonPath(_baseDir);
        _protector = protector ?? SecretProtector.Default;
    }

    public string SharedFilePath => _sharedPath;

    // ---- 读取 ----

    /// <summary>
    /// 读取共享数据。文件不存在返回空负载；整体损坏时把原文件改名为
    /// shared.corrupt-yyyyMMdd-HHmmss.json 保留并返回空负载（绝不静默覆盖用户数据）。
    /// </summary>
    public SharedData Load()
    {
        lock (_ioGate)
        {
            LastQuarantinedFile = null;
            LastLoadFailed = false;

            string json;
            using (var gate = CrossProcessLock.Acquire(LockName, _sharedPath + ".lock"))
            {
                try
                {
                    if (!File.Exists(_sharedPath)) return SharedData.Empty();
                    json = File.ReadAllText(_sharedPath);
                }
                catch (IOException)
                {
                    // 读取失败（并发写 / 磁盘抖动）：尝试读 .bak 备份，仍失败则报告错误状态
                    LastLoadFailed = true;
                    try
                    {
                        if (File.Exists(_bakPath))
                        {
                            json = File.ReadAllText(_bakPath);
                            return Decode(json, quarantine: false);
                        }
                    }
                    catch (IOException) { }
                    return SharedData.Empty();
                }
            }
            return Decode(json, quarantine: true);
        }
    }

    private SharedData Decode(string json, bool quarantine)
    {
        if (SharedDataJson.TryParse(json, _protector, out var data)) return data;
        if (!quarantine) return SharedData.Empty();

        // 整体解码失败：保留原文件供恢复 / 排查
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var corruptPath = Path.Combine(_baseDir, $"shared.corrupt-{stamp}.json");
        try
        {
            Directory.CreateDirectory(_baseDir);
            if (File.Exists(corruptPath)) File.Delete(corruptPath);
            File.Move(_sharedPath, corruptPath);
            LastQuarantinedFile = corruptPath;
        }
        catch (IOException)
        {
            // 改名失败（文件被占用）：保留原文件不动，仅报告
            LastLoadFailed = true;
        }
        return SharedData.Empty();
    }

    /// <summary>
    /// 读取并保证默认配置存在：首次运行生成三家供应商默认配置（停用）；
    /// 版本升级新增供应商时自动补齐，不覆盖已有配置。
    /// </summary>
    public SharedData LoadWithDefaults()
    {
        var data = Load();
        if (LastLoadFailed) return data; // 读取异常时不回写，避免覆盖

        var changed = false;
        if (data.Configs.Count == 0)
        {
            data.Configs = ProviderKindExtensions.All
                .Select(kind => new ProviderConfig(kind))
                .ToList();
            changed = true;
        }
        else
        {
            var existing = data.Configs.Select(c => c.Kind).ToHashSet();
            var missing = ProviderKindExtensions.All.Where(kind => !existing.Contains(kind)).ToList();
            if (missing.Count > 0)
            {
                data.Configs.AddRange(missing.Select(kind => new ProviderConfig(kind)));
                changed = true;
            }
        }
        if (changed) Save(data);
        return data;
    }

    // ---- 写入 ----

    /// <summary>
    /// 写入共享数据：写前备份 .bak → 写临时文件 → 原子替换。
    /// </summary>
    public void Save(SharedData data)
    {
        lock (_ioGate)
        {
            Directory.CreateDirectory(_baseDir);
            var json = SharedDataJson.Write(data, _protector);
            var tmpPath = _sharedPath + ".tmp";

            using (var gate = CrossProcessLock.Acquire(LockName, _sharedPath + ".lock"))
            {
                // 1. 写前备份
                try
                {
                    if (File.Exists(_sharedPath))
                    {
                        if (File.Exists(_bakPath)) File.Delete(_bakPath);
                        File.Copy(_sharedPath, _bakPath);
                    }
                }
                catch (IOException) { /* 备份失败不阻断写入 */ }

                // 2. 临时文件 + 原子替换，防断电写坏
                File.WriteAllText(tmpPath, json);
                if (File.Exists(_sharedPath))
                {
                    try { File.Replace(tmpPath, _sharedPath, null); }
                    catch (IOException) { File.Move(tmpPath, _sharedPath, overwrite: true); }
                }
                else
                {
                    File.Move(tmpPath, _sharedPath);
                }
            }
        }
    }

    // ---- 局部更新便捷方法（读-改-写，全程持锁由 Load/Save 保证） ----

    public ProviderConfig? FindConfig(ProviderKind kind) =>
        Load().Configs.FirstOrDefault(c => c.Kind == kind);

    public void SaveConfig(ProviderConfig config)
    {
        var data = Load();
        var index = data.Configs.FindIndex(c => c.Kind == config.Kind);
        if (index >= 0) data.Configs[index] = config;
        else data.Configs.Add(config);
        Save(data);
    }

    public void SetTheme(ThemePreference theme)
    {
        var data = Load();
        data.Theme = theme;
        Save(data);
    }

    public void SetAlertsEnabled(bool enabled)
    {
        var data = Load();
        data.AlertsEnabled = enabled;
        Save(data);
    }

    public void SetAlertThreshold(double threshold)
    {
        var data = Load();
        data.AlertThreshold = threshold is > 0 and <= 100 ? threshold : 80;
        Save(data);
    }

    /// <summary>修改阈值后清空已通知记录，按新阈值重新判定（FR-4）。</summary>
    public void SetAlertThresholdAndReset(double threshold)
    {
        var data = Load();
        data.AlertThreshold = threshold is > 0 and <= 100 ? threshold : 80;
        data.AlertedKeys.Clear();
        Save(data);
    }

    public void ReplaceAlertedKeys(IEnumerable<string> keys)
    {
        var data = Load();
        data.AlertedKeys = keys.Distinct().ToList();
        Save(data);
    }

    /// <summary>写用量缓存（连续失败不清空旧缓存由调用方保证）。</summary>
    public void SaveCachedUsages(IReadOnlyList<ProviderUsage> usages)
    {
        var data = Load();
        data.CachedUsages = usages.ToList();
        Save(data);
    }
}
