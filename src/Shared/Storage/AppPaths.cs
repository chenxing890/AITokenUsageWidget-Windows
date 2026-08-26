namespace AITokenUsageWidget.Shared.Storage;

/// <summary>数据目录与文件路径（%LOCALAPPDATA%\AITokenUsageWidget，FR-5）。</summary>
public static class AppPaths
{
    /// <summary>测试 / 特殊部署可用环境变量覆盖数据目录。</summary>
    public const string DataDirEnvVar = "AITOKENWIDGET_DATA_DIR";

    public static string DefaultBaseDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(DataDirEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
            // Windows 上 LocalApplicationData == %LOCALAPPDATA%
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AITokenUsageWidget");
        }
    }

    public static string SharedJsonPath(string? baseDir = null) =>
        Path.Combine(baseDir ?? DefaultBaseDir, "shared.json");

    public static string BackupJsonPath(string? baseDir = null) =>
        SharedJsonPath(baseDir) + ".bak";

    public static string LogsDir(string? baseDir = null) =>
        Path.Combine(baseDir ?? DefaultBaseDir, "logs");

    public static void EnsureDirectory(string? baseDir = null) =>
        Directory.CreateDirectory(baseDir ?? DefaultBaseDir);
}
