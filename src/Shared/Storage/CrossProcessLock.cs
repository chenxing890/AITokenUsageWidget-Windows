using System.Collections.Concurrent;

namespace AITokenUsageWidget.Shared.Storage;

/// <summary>
/// 跨进程读写锁：Windows 使用命名 Mutex（Local\ 命名空间），其余平台（单测）
/// 退化为进程内锁 + 独占文件句柄。获取失败（锁冲突）时短暂重试（最多 5 次，间隔 200ms，FR-5）。
/// 最终失败返回 null —— 调用方必须按「未持锁」处理：读视为失败、写直接跳过，
/// 绝不在无锁状态下裸读写共享文件（否则并发读写窗口期会读到不存在的文件并回写空配置）。
/// </summary>
public static class CrossProcessLock
{
    private const int RetryCount = 5;
    private const int RetryDelayMs = 200;

    public static IDisposable? Acquire(string name, string? lockFilePath = null, int timeoutPerTryMs = 100)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryDelayMs);
            IDisposable? held = OperatingSystem.IsWindows()
                ? NamedMutexLock.TryAcquire(name, timeoutPerTryMs)
                : LockFileLock.TryAcquire(lockFilePath ?? Path.GetTempPath() + name + ".lock", timeoutPerTryMs);
            if (held != null) return held;
        }
        return null;
    }

    /// <summary>命名 Mutex 实现（Windows）。持锁期间 Mutex 归本线程所有。</summary>
    private sealed class NamedMutexLock : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _released;

        public static NamedMutexLock? TryAcquire(string name, int timeoutMs)
        {
            var mutex = new Mutex(initiallyOwned: false, name: "Local\\" + name);
            try
            {
                // AbandonedMutexException 时锁实际上已被本线程获得，可直接使用
                if (!mutex.WaitOne(timeoutMs))
                {
                    mutex.Dispose();
                    return null; // 超时未取得锁：必须向上返回失败，绝不能当作已持锁
                }
                return new NamedMutexLock(mutex);
            }
            catch (AbandonedMutexException)
            {
                return new NamedMutexLock(mutex);
            }
            catch
            {
                mutex.Dispose();
                return null;
            }
        }

        private NamedMutexLock(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
                _mutex.Dispose();
            }
        }
    }

    /// <summary>独占文件句柄实现（非 Windows / 单测）。同进程内再叠加进程内锁避免重入。</summary>
    private sealed class LockFileLock : IDisposable
    {
        private static readonly ConcurrentDictionary<string, object> ProcessLocks = new();
        private readonly object _processLock;
        private readonly FileStream? _stream;

        public static LockFileLock? TryAcquire(string path, int timeoutMs)
        {
            var processLock = ProcessLocks.GetOrAdd(path, _ => new object());
            if (!Monitor.TryEnter(processLock, timeoutMs)) return null;
            try
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (true)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        return new LockFileLock(processLock, stream);
                    }
                    catch (IOException) when (Environment.TickCount64 < deadline)
                    {
                        Thread.Sleep(15);
                    }
                }
            }
            catch
            {
                Monitor.Exit(processLock);
                return null;
            }
        }

        private LockFileLock(object processLock, FileStream? stream)
        {
            _processLock = processLock;
            _stream = stream;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            Monitor.Exit(_processLock);
        }
    }
}
