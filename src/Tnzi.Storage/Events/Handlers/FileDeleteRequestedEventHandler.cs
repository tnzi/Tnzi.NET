namespace Tnzi.Storage.Events.Handlers;

/// <summary>
/// 文件删除请求事件处理器
/// 仅负责删除物理文件（数据库记录由服务层处理）
/// </summary>
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
        try
        {
            _logger.LogInformation("Start deleting physical file: {FileId}, Path: {FilePath}",
                @event.FileId, @event.FilePath);

            // 删除物理文件
            if (!string.IsNullOrEmpty(@event.FilePath))
            {
                try
                {
                    await _storage.DeleteAsync(@event.FilePath);
                    _logger.LogDebug("Physical file deleted: {FilePath}", @event.FilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete physical file: {FilePath}", @event.FilePath);
                }
            }

            // 删除缩略图
            if (!string.IsNullOrEmpty(@event.ThumbnailPath))
            {
                try
                {
                    await _storage.DeleteAsync(@event.ThumbnailPath);
                    _logger.LogDebug("Thumbnail deleted: {ThumbnailPath}", @event.ThumbnailPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete thumbnail: {ThumbnailPath}", @event.ThumbnailPath);
                }
            }

            _logger.LogInformation("Physical file deletion completed: {FileId}", @event.FileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while deleting physical file: {FileId}", @event.FileId);
            // 失败的删除将由定期清理任务处理，不抛出异常
        }
    }
}

