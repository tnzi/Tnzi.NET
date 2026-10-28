namespace Tnzi.Storage.Events.Handlers;

/// <summary>
/// 文件删除请求事件处理器：只删事件里点名的物理对象。
/// 记录由发布事件的那条路径处理：解绑归零时它已在同一个事务里删掉了记录；建版本 / 恢复版本时记录还在，只是不再指向旧缩略图。
/// </summary>
/// <remarks>
/// ★ 删除失败必须以异常离开：事件总线的重试与死信只在处理器抛出时才会发生。整体吞掉再记一条日志，
/// 等于告诉总线「处理成功」—— 而对「只剩对象、没有记录指向」的那一类删除（例如换版本后的旧缩略图），
/// 没有任何清理任务能再找到它，一次瞬时故障就是一个永久孤儿对象。
/// 两个对象都会先各试一次再抛，免得正文删失败连带缩略图一次都没试；provider 的删除是幂等的，重试整条事件是安全的。
/// 「失败」按对象是否仍在判（<see cref="StoredObjectDeletion"/>）：内置 provider 把故障吞成 <c>false</c>，
/// 而 <c>false</c> 也可能只是对象本来就不在。
/// </remarks>
public class FileDeleteRequestedEventHandler : IEventHandler<FileDeleteRequestedEvent>
{
    private readonly IFileStorage _storage;
    private readonly ILogger<FileDeleteRequestedEventHandler> _logger;

    public FileDeleteRequestedEventHandler(
        IFileStorage storage,
        ILogger<FileDeleteRequestedEventHandler> logger)
    {
        _storage = Check.NotNull(storage);
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(FileDeleteRequestedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start deleting physical file: {FileId}, Path: {FilePath}",
            @event.FileId, @event.FilePath);

        List<Exception>? failures = null;

        foreach (var path in new[] { @event.FilePath, @event.ThumbnailPath })
        {
            if (string.IsNullOrEmpty(path))
                continue;

            try
            {
                // 内置 provider 遇到故障不抛而是报 false，所以只看「有没有抛」等于把瞬时故障当成删除成功。
                if (await StoredObjectDeletion.DeleteAndConfirmAsync(_storage, path))
                {
                    _logger.LogDebug("Stored object deleted: {Path}", path);
                }
                else
                {
                    (failures ??= []).Add(new StorageException(
                        $"Stored object '{path}' is still present after the delete request.", path, httpStatusCode: 500));
                }
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                $"Failed to delete {failures.Count} stored object(s) of file {@event.FileId}; the event bus retries the deletion.",
                failures);
        }

        _logger.LogInformation("Physical file deletion completed: {FileId}", @event.FileId);
    }
}
