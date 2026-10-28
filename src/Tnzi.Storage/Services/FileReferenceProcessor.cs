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
                // 引用表只存得下 Guid 主键。跳过 = 没有引用行、计数不加，实体照样保存，
                // 它引用的临时上传随后被当成孤儿删掉。拒绝这次保存（理由见 FileFieldHostKeyGuard，启动时已对注册过的实体查过一遍）。
                if (!Guid.TryParse(change.EntityId, out var entityIdGuid))
                {
                    throw new InvalidOperationException(
                        FileFieldHostKeyGuard.DescribeUnsupportedReference(change.EntityType, change.EntityId, change.FieldName));
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

                if (!existing)
                {
                    // 原子 +1（理由见 FileReferenceCounts）：两条记录并发绑定同一份文件，各加各的，谁也不覆盖谁。
                    var count = await FileReferenceCounts.AddAsync(_fileRepository, change.FileId, cancellationToken);
                    if (count == null)
                    {
                        _logger.LogWarning("FileRecord not found for FileId={FileId}", change.FileId);
                        continue;
                    }

                    _logger.LogDebug("FileRecord.ReferenceCount updated to {Count}", count);
                }

                if (!change.IsPublicFile)
                    continue;

                // 字段声明为 [FileField(Public = true)]（头像 / 站点素材）→ 文件即刻公开可读。
                // 意图挂在字段上而非调用方：写头像的路径有很多条，任何一条忘记传参都会让头像 404。
                // 只升不降 —— 移除引用时不写回 false，因为同一个文件可能仍被别处公开引用；
                // 收回公开须显式走 IFileStorageService.SetFileVisibilityAsync。
                var fileRecord = await _fileRepository.GetAsync(change.FileId, cancellationToken);
                if (fileRecord != null && !fileRecord.IsPublic)
                {
                    fileRecord.IsPublic = true;
                    await _fileRepository.UpdateAsync(fileRecord, cancellationToken);
                    _logger.LogInformation(
                        "File marked public by declared field: FileId={FileId}, Entity={EntityType}, Field={FieldName}",
                        change.FileId, change.EntityType, change.FieldName);
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
                // 非 Guid 主键建引用时就被拒绝了，这样的引用行不可能存在：没有可释放的东西。
                if (!Guid.TryParse(change.EntityId, out var entityIdGuid))
                {
                    _logger.LogDebug("EntityId '{EntityId}' is not a Guid; no file reference can exist for it, nothing to release", change.EntityId);
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

                    // 原子 -1，不低于 0
                    await FileReferenceCounts.ReleaseAsync(_fileRepository, change.FileId, cancellationToken);

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
    /// 解绑之后已无人持有的文件：在当前事务里删掉记录，再请求删除它的对象（事务感知发布，提交之后由处理器执行）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>记录与对象一起走。</b>此前这里只发一个删对象的事件，记录原样留着：处理器只删字节，而这条路径上没有任何代码删记录，
    /// 唯一会收它的孤儿回收默认关闭。留下的是一条指向空处的记录，列表、统计、用量照计，完整性校验报 Missing，
    /// 下载在本地 404、在对象存储上 500；任何实体再写入这个 id，计数 0→1，读权限探针因记录存在而放行，实体指向不存在的字节。
    /// </para>
    /// <para>
    /// 删除是<b>条件删除</b>，判据在数据库里求值：计数仍 &lt;= 0、没有正式引用行（计数与引用行对不上时不按计数删）、
    /// 不是归档进目录的存量临时记录（与孤儿回收同一条豁免）。删掉了才发事件；期间又被绑定、或判据不成立，就什么都不做。
    /// 记录一旦删除，之后的绑定找不到它（读权限探针答否），不会再有实体指向一个即将被删掉字节的 id。
    /// </para>
    /// <para>
    /// 没有事件总线时记录也不删：没有人会去删对象，先删记录只会留下一个谁也找不到的对象。交给孤儿回收，它连记录带对象一起收。
    /// 引用追踪关闭时同样什么都不做：计数没被维护，字段清空不等于文件该删。
    /// </para>
    /// </remarks>
    public async Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.EnableFileReference)
            return;

        if (_eventBus == null)
            return;

        var fileIds = changes
            .Where(c => c.ChangeType == FileReferenceChangeType.Delete)
            .Select(c => c.FileId)
            .Distinct()
            .ToList();

        foreach (var fileId in fileIds)
        {
            try
            {
                var removed = await TryDeleteUnheldRecordAsync(fileId, cancellationToken);
                if (removed == null)
                    continue;

                await _eventBus.PublishAsync(new FileDeleteRequestedEvent
                {
                    FileId = fileId,
                    FilePath = removed.Path,
                    ThumbnailPath = removed.ThumbnailPath,
                    Provider = removed.Provider ?? "Local"
                }, cancellationToken);
                await _eventBus.PublishAsync(new FileRecordDeletedEvent { FileId = fileId }, cancellationToken);

                _logger.LogInformation("File record removed after its last reference was released: FileId={FileId}", fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove an unreferenced file after unbinding: FileId={FileId}", fileId);
                // 不抛出异常：业务实体已经保存成功，这一步失败不应让它回滚。
                // 记录与对象都还在（条件删除没生效，或事务随后回滚），孤儿回收会再收一次。
            }
        }
    }

    /// <summary>删掉的那条记录名下的对象，供删除事件使用。</summary>
    private sealed record RemovedFileObjects(string? Path, string? ThumbnailPath, string? Provider);

    /// <summary>
    /// 按判据条件删除一条已无人持有的记录；删掉了返回它名下的对象路径，没删返回 null。
    /// </summary>
    private async Task<RemovedFileObjects?> TryDeleteUnheldRecordAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var snapshot = await _fileRepository.AsQueryable()
            .Where(f => f.Id == fileId)
            .Select(f => new { f.Path, f.ThumbnailPath, f.Provider, f.ReferenceCount })
            .FirstOrDefaultAsync(cancellationToken);
        if (snapshot == null || snapshot.ReferenceCount > 0)
            return null;

        // 判据在数据库里求值、与删除是同一条语句（见 FileReferenceCounts.TryDeleteUnheldAsync）；
        // 剩下的临时引用行与变更跟踪器里挂着的这一行一并收掉。
        if (!await FileReferenceCounts.TryDeleteUnheldAsync(_fileRepository, _referenceRepository, fileId, cancellationToken))
            return null;

        return new RemovedFileObjects(snapshot.Path, snapshot.ThumbnailPath, snapshot.Provider);
    }
}
