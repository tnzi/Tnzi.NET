namespace Tnzi.AI.Channels.Store;

/// <summary>
/// 基于 JSON 文件的线程映射存储 — 适用于开发和简单部署场景。
/// 使用 tempfile -> rename 原子写入防止数据损坏。
/// </summary>
public class FileChannelThreadStore : IChannelThreadStore
{
    private readonly ILogger<FileChannelThreadStore> _logger;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);

    // 内存缓存（Singleton 生命周期）
    private Dictionary<string, Guid>? _cache;

    public FileChannelThreadStore(ILogger<FileChannelThreadStore> logger, string filePath)
    {
        _logger = Check.NotNull(logger);
        _filePath = Check.NotNullOrWhiteSpace(filePath);
    }

    public async Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null)
    {
        // Intentional lock-free read fast path: writes (Set/Remove) swap in a brand-new immutable
        // dictionary under _lock, so a concurrent read either sees the old or the new snapshot
        // atomically — never a half-mutated map. No lock acquisition is needed on the hot read path.
        var mappings = await LoadAsync();
        var key = BuildKey(channelName, chatId, topicId);
        return mappings.TryGetValue(key, out var threadId) ? threadId : null;
    }

    public async Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var mappings = await LoadAsync();
            var key = BuildKey(channelName, chatId, topicId);
            // 创建新字典（不可变模式）
            var updated = new Dictionary<string, Guid>(mappings) { [key] = threadId };
            await SaveAtomicAsync(updated);
            _cache = updated;
            _logger.LogDebug("Set channel thread mapping: {Key} -> {ThreadId}", key, threadId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string channelName, string chatId, string? topicId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var mappings = await LoadAsync();
            var key = BuildKey(channelName, chatId, topicId);
            if (!mappings.ContainsKey(key)) return;

            var updated = new Dictionary<string, Guid>(mappings);
            updated.Remove(key);
            await SaveAtomicAsync(updated);
            _cache = updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string BuildKey(string channelName, string chatId, string? topicId)
    {
        return topicId != null ? $"{channelName}:{chatId}:{topicId}" : $"{channelName}:{chatId}";
    }

    private async Task<Dictionary<string, Guid>> LoadAsync()
    {
        if (_cache != null) return _cache;

        if (!File.Exists(_filePath))
        {
            _cache = new Dictionary<string, Guid>();
            return _cache;
        }

        var json = await File.ReadAllTextAsync(_filePath);
        _cache = JsonSerializer.Deserialize<Dictionary<string, Guid>>(json) ?? new();
        return _cache;
    }

    /// <summary>
    /// 原子写入：写入临时文件 -> 重命名替换目标文件
    /// </summary>
    private async Task SaveAtomicAsync(Dictionary<string, Guid> mappings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempFile = _filePath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(mappings, WriteOptions);
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _filePath, overwrite: true);
        }
        catch
        {
            // 清理临时文件
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
