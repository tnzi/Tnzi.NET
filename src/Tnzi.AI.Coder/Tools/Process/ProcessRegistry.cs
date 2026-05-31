namespace Tnzi.AI.Coder.ProcessManagement;

/// <summary>
/// 进程注册表 — 管理后台进程的静态注册表，供 ProcessTools 和 ShellTools 共享
/// </summary>
internal static class ProcessRegistry
{
    private static readonly ConcurrentDictionary<string, ManagedProcess> _processes = new();
    private static int _processCounter;

    /// <summary>
    /// 已退出进程的最大保留时间
    /// </summary>
    private static readonly TimeSpan ExitedProcessRetention = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 获取所有进程
    /// </summary>
    public static IEnumerable<ManagedProcess> All => _processes.Values;

    /// <summary>
    /// 创建一个 ManagedProcess 实例（不注册到注册表）
    /// </summary>
    public static ManagedProcess CreateManagedProcess(string command, ProcessStartInfo psi, long maxOutputSize)
    {
        var managed = new ManagedProcess
        {
            Command = command,
            StartTime = DateTime.UtcNow,
            Process = new Process { StartInfo = psi }
        };

        managed.Process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (managed.Stdout)
                {
                    if (managed.Stdout.Length < maxOutputSize)
                    {
                        managed.Stdout.AppendLine(e.Data);
                    }
                }
            }
        };

        managed.Process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (managed.Stderr)
                {
                    if (managed.Stderr.Length < maxOutputSize)
                    {
                        managed.Stderr.AppendLine(e.Data);
                    }
                }
            }
        };

        return managed;
    }

    /// <summary>
    /// 注册进程并返回 ID
    /// </summary>
    public static string Register(ManagedProcess managed)
    {
        var processId = $"proc-{Interlocked.Increment(ref _processCounter)}";
        managed.Id = processId;
        _processes[processId] = managed;
        return processId;
    }

    /// <summary>
    /// 获取指定 ID 的进程
    /// </summary>
    public static ManagedProcess? Get(string processId)
    {
        _processes.TryGetValue(processId, out var managed);
        return managed;
    }

    /// <summary>
    /// 移除指定 ID 的进程
    /// </summary>
    public static bool Remove(string processId, out ManagedProcess? managed)
    {
        return _processes.TryRemove(processId, out managed);
    }

    /// <summary>
    /// 获取正在运行的进程数
    /// </summary>
    public static int RunningCount
    {
        get
        {
            var count = 0;
            foreach (var mp in _processes.Values)
            {
                try
                {
                    if (!mp.Process.HasExited) count++;
                }
                catch
                {
                    // 忽略
                }
            }

            return count;
        }
    }

    /// <summary>
    /// 清理已退出超过保留时间的进程
    /// </summary>
    public static void CleanupExited()
    {
        var cutoff = DateTime.UtcNow - ExitedProcessRetention;

        foreach (var kvp in _processes)
        {
            try
            {
                if (kvp.Value.Process.HasExited && kvp.Value.StartTime < cutoff)
                {
                    if (_processes.TryRemove(kvp.Key, out var removed))
                    {
                        removed.Process.Dispose();
                    }
                }
            }
            catch
            {
                // 进程已销毁，直接移除
                if (_processes.TryRemove(kvp.Key, out var removed))
                {
                    try { removed.Process.Dispose(); }
                    catch { /* 忽略 */ }
                }
            }
        }
    }

    /// <summary>
    /// 终止并清理所有托管进程（应用关闭时调用）
    /// </summary>
    public static void KillAllAndCleanup()
    {
        foreach (var kvp in _processes)
        {
            if (_processes.TryRemove(kvp.Key, out var managed))
            {
                try
                {
                    if (!managed.Process.HasExited)
                    {
                        managed.Process.Kill(entireProcessTree: true);
                    }
                }
                catch { /* best effort */ }

                try { managed.Process.Dispose(); }
                catch { /* best effort */ }
            }
        }
    }
}
