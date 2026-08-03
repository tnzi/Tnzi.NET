namespace Tnzi.Storage.Services;

/// <summary>
/// 文件引用处理器实现
/// 由 EFCore / TnziDbContext 调用，在事务中处理文件引用变更
/// </summary>
public class FileReferenceProcessor : IFileReferenceProcessor
{
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<FileReferenceProcessor> _logger;
    private readonly IOptionsMonitor<StorageOptions> _options;

    public FileReferenceProcessor(
        IRepository<FileReference, Guid> referenceRepository,
        IRepository<FileRecord, Guid> fileRepository,
        ILogger<FileReferenceProcessor> logger,
        IOptionsMonitor<StorageOptions> options,
        IEventBus? eventBus = null)
    {
        _referenceRepository = Check.NotNull(referenceRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
        _eventBus = eventBus;
    }

    /// <summary>
    /// 处理文件引用变更（在数据库事务中调用）
    /// </summary>
    public async Task ProcessChangesAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        // 引用追踪开关：关闭时跳过自动 [FileField] 追踪与手动引用处理
        if (!_options.CurrentValue.EnableFileReference)
            return;

        if (!changes.Any())
            return;

        var createChanges = changes.Where(c => c.ChangeType == FileReferenceChangeType.Create).ToList();
        var deleteChanges = changes.Where(c => c.ChangeType == FileReferenceChangeType.Delete).ToList();

        // 1. 处理新增引用
        foreach (var change in createChanges)
        {
            try
            {
                if (!Guid.TryParse(change.EntityId, out var entityIdGuid))
                {
                    _logger.LogWarning("Cannot parse EntityId '{EntityId}' as Guid, skipping file reference", change.EntityId);
                    continue;
                }

                // 检查是否已存在相同引用（避免重复创建）
                var existing = await _referenceRepository.AsQueryable()
                    .Where(r => r.FileId == change.FileId
                             && r.EntityType == change.EntityType
                             && r.EntityId == entityIdGuid
                             && r.FieldName == change.FieldName)
                    .AnyAsync(cancellationToken);

                if (!existing)
                {
                    var reference = new FileReference
                    {
                        FileId = change.FileId,
                        EntityType = change.EntityType,
                        EntityId = entityIdGuid,
                        FieldName = change.FieldName,
                        IsTemporary = false
                    };

                    await _referenceRepository.InsertAsync(reference, cancellationToken);

                    _logger.LogDebug("Created file reference: FileId={FileId}, Entity={EntityType}/{EntityId}, Field={FieldName}",
                        change.FileId, change.EntityType, change.EntityId, change.FieldName);
                }

                // 引用已在且字段没有声明公开 → 无需回读文件记录（SaveChanges 热路径）。
                // 引用计数只在真正新建引用时递增；公开标记则每次都对齐 ——
                // 后者是幂等的字段声明（见下），重存一次实体即可修好历史遗留的记录。
                if (existing && !change.IsPublicFile)
                    continue;

                var fileRecord = await _fileRepository.GetAsync(change.FileId, cancellationToken);
                if (fileRecord == null)
                {
                    if (!existing)
                        _logger.LogWarning("FileRecord not found for FileId={FileId}", change.FileId);
                    continue;
                }

                var recordChanged = false;

                if (!existing)
                {
                    fileRecord.ReferenceCount++;
                    recordChanged = true;
                    _logger.LogDebug("FileRecord.ReferenceCount updated to {Count}", fileRecord.ReferenceCount);
                }

                // 字段声明为 [FileField(Public = true)]（头像 / 站点素材）→ 文件即刻公开可读。
                // 意图挂在字段上而非调用方：写头像的路径有很多条，任何一条忘记传参都会让头像 404。
                // 只升不降 —— 移除引用时不写回 false，因为同一个文件可能仍被别处公开引用；
                // 收回公开须显式走 IFileStorageService.SetFileVisibilityAsync。
                if (change.IsPublicFile && !fileRecord.IsPublic)
                {
                    fileRecord.IsPublic = true;
                    recordChanged = true;
                    _logger.LogInformation(
                        "File marked public by declared field: FileId={FileId}, Entity={EntityType}, Field={FieldName}",
                        change.FileId, change.EntityType, change.FieldName);
                }

                if (recordChanged)
                {
                    await _fileRepository.UpdateAsync(fileRecord, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file reference: FileId={FileId}", change.FileId);
                throw; // 在事务中抛出以触发回滚
            }
        }

        // 2. 处理删除引用
        foreach (var change in deleteChanges)
        {
            try
            {
                if (!Guid.TryParse(change.EntityId, out var entityIdGuid))
                {
                    _logger.LogWarning("Cannot parse EntityId '{EntityId}' as Guid, skipping file reference deletion", change.EntityId);
                    continue;
                }

                // 查找并删除引用
                var reference = await _referenceRepository.AsQueryable()
                    .Where(r => r.FileId == change.FileId
                             && r.EntityType == change.EntityType
                             && r.EntityId == entityIdGuid
                             && r.FieldName == change.FieldName)
                    .FirstOrDefaultAsync(cancellationToken);

                if (reference != null)
                {
                    await _referenceRepository.DeleteAsync(reference, cancellationToken);

                    // 减少引用计数
                    var fileRecord = await _fileRepository.GetAsync(change.FileId, cancellationToken);
                    if (fileRecord != null)
                    {
                        fileRecord.ReferenceCount = Math.Max(0, fileRecord.ReferenceCount - 1);
                        await _fileRepository.UpdateAsync(fileRecord, cancellationToken);
                    }

                    _logger.LogDebug("Deleted file reference: FileId={FileId}, Entity={EntityType}/{EntityId}, Field={FieldName}",
                        change.FileId, change.EntityType, change.EntityId, change.FieldName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file reference: FileId={FileId}", change.FileId);
                throw;
            }
        }
    }

    /// <summary>
    /// 发布文件删除事件（在事务成功后调用）
    /// </summary>
    public async Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        if (_eventBus == null)
            return;

        var deleteChanges = changes.Where(c => c.ChangeType == FileReferenceChangeType.Delete).ToList();
        if (!deleteChanges.Any())
            return;

        var fileIds = deleteChanges.Select(c => c.FileId).Distinct().ToList();

        foreach (var fileId in fileIds)
        {
            try
            {
                // 检查文件引用计数
                var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
                if (fileRecord != null && fileRecord.ReferenceCount <= 0)
                {
                    // 发布删除事件
                    await _eventBus.PublishAsync(new FileDeleteRequestedEvent
                    {
                        FileId = fileId,
                        FilePath = fileRecord.Path,
                        ThumbnailPath = fileRecord.ThumbnailPath,
                        Provider = fileRecord.Provider ?? "Local"
                    }, cancellationToken);

                    _logger.LogInformation("Published file delete event: FileId={FileId}", fileId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish file delete event: FileId={FileId}", fileId);
                // 不抛出异常，删除事件发布失败不应影响主操作
                // 僵尸文件将由清理任务处理
            }
        }
    }
}
