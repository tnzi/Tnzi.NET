namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 配置变更检测器 — 基于文件 mtime 追踪，触发缓存失效
/// </summary>
public interface IConfigChangeDetector : IDisposable
{
    /// <summary>监视单个文件的变更</summary>
    void Watch(string filePath, Func<Task> onChanged);

    /// <summary>监视目录中匹配的文件变更</summary>
    void WatchDirectory(string directoryPath, string pattern, Func<Task> onChanged);

    /// <summary>取消监视</summary>
    void Unwatch(string path);

    /// <summary>主动检查所有监视项的变更</summary>
    Task CheckForChangesAsync();
}
