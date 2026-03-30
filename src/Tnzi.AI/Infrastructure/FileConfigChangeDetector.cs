namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 基于文件 mtime 的配置变更检测器。
/// 提供 Watch/WatchDirectory 追踪 + 定期 CheckForChangesAsync 轮询。
/// 另提供 WriteAtomicAsync 静态工具方法（tempfile -> rename）。
/// </summary>
public class FileConfigChangeDetector : IConfigChangeDetector
{
    private readonly ILogger<FileConfigChangeDetector> _logger;
    private readonly ConcurrentDictionary<string, WatchEntry> _watches = new(StringComparer.OrdinalIgnoreCase);

    private record WatchEntry(Func<Task> OnChanged, DateTime LastMtime, bool IsDirectory, string? Pattern);

    public FileConfigChangeDetector(ILogger<FileConfigChangeDetector> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public void Watch(string filePath, Func<Task> onChanged)
    {
        Check.NotNullOrWhiteSpace(filePath);
        Check.NotNull(onChanged);

        var mtime = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.MinValue;
        _watches[filePath] = new WatchEntry(onChanged, mtime, IsDirectory: false, Pattern: null);
    }

    public void WatchDirectory(string directoryPath, string pattern, Func<Task> onChanged)
    {
        Check.NotNullOrWhiteSpace(directoryPath);
        Check.NotNull(onChanged);

        var mtime = GetDirectoryMaxMtime(directoryPath, pattern);
        _watches[directoryPath] = new WatchEntry(onChanged, mtime, IsDirectory: true, Pattern: pattern);
    }

    public void Unwatch(string path)
    {
        _watches.TryRemove(path, out _);
    }

    public async Task CheckForChangesAsync()
    {
        foreach (var (path, entry) in _watches)
        {
            try
            {
                var currentMtime = entry.IsDirectory
                    ? GetDirectoryMaxMtime(path, entry.Pattern)
                    : (File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue);

                if (currentMtime > entry.LastMtime)
                {
                    // 更新 mtime（不可变 — 替换条目）
                    _watches[path] = entry with { LastMtime = currentMtime };
                    _logger.LogDebug("Config change detected: {Path} (mtime: {Old} -> {New})", path, entry.LastMtime, currentMtime);
                    await entry.OnChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking config change for {Path}", path);
            }
        }
    }

    /// <summary>
    /// 原子文件写入：写入临时文件 -> 重命名替换目标文件。
    /// 防止在写入过程中读取到不完整内容。
    /// </summary>
    public static async Task WriteAtomicAsync(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempFile = filePath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempFile, content);
            File.Move(tempFile, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }

    public void Dispose()
    {
        _watches.Clear();
    }

    private static DateTime GetDirectoryMaxMtime(string directoryPath, string? pattern)
    {
        if (!Directory.Exists(directoryPath)) return DateTime.MinValue;

        var files = Directory.GetFiles(directoryPath, pattern ?? "*", SearchOption.AllDirectories);
        if (files.Length == 0) return Directory.GetLastWriteTimeUtc(directoryPath);

        return files.Max(f => File.GetLastWriteTimeUtc(f));
    }
}
