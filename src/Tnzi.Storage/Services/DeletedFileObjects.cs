namespace Tnzi.Storage.Services;

/// <summary>
/// 一条<b>已经删掉记录</b>的文件，收掉它在存储里的正文与缩略图。后台清理与系统身份的释放共用这一份。
/// </summary>
/// <remarks>
/// ★ 记录在这之前就已条件删除（先删记录、删掉了才删对象，理由见 <see cref="FileReferenceCounts"/>），
/// 所以这里删不掉的对象再也没有记录可以找回它，下一趟清理按记录枚举永远看不见它。
/// 没删掉的对象交给 <see cref="FileDeleteRequestedEvent"/>：它的处理器在对象仍在时抛出，
/// 事件总线的重试与死信接着管。没有事件总线时只能记 Warning。
/// </remarks>
internal static class DeletedFileObjects
{
    public static async Task DisposeAsync(
        IFileStorage storage, IEventBus? eventBus, ILogger logger, FileRecord fileRecord, CancellationToken cancellationToken = default)
    {
        var remainingPath = await TryDeleteObjectAsync(storage, logger, fileRecord.Path);
        var remainingThumbnail = await TryDeleteObjectAsync(storage, logger, fileRecord.ThumbnailPath);
        if (remainingPath == null && remainingThumbnail == null)
            return;

        if (eventBus == null)
        {
            logger.LogWarning("Stored objects of deleted file {FileId} could not be removed and no event bus is available to retry: {Path} {ThumbnailPath}",
                fileRecord.Id, remainingPath, remainingThumbnail);
            return;
        }

        await eventBus.PublishAsync(new FileDeleteRequestedEvent
        {
            FileId = fileRecord.Id,
            FilePath = remainingPath,
            ThumbnailPath = remainingThumbnail,
            Provider = fileRecord.Provider
        }, cancellationToken);
    }

    /// <summary>删掉一个对象；返回仍留在存储里的路径（删掉了或本来就不在时为 null）。</summary>
    private static async Task<string?> TryDeleteObjectAsync(IFileStorage storage, ILogger logger, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            if (await StoredObjectDeletion.DeleteAndConfirmAsync(storage, path))
                return null;

            logger.LogWarning("Stored object is still present after deletion, handing it to the delete retry: {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to delete stored object, handing it to the delete retry: {Path}, Error={Error}", path, ex.Message);
        }

        return path;
    }
}
