namespace Tnzi.Storage.Services;

/// <summary>
/// 文件引用管理服务实现
/// </summary>
public class FileReferenceService : ApplicationService, IFileReferenceService
{
    private readonly IRepository<FileRecord, Guid> _repository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;

    public FileReferenceService(
        IRepository<FileRecord, Guid> repository,
        IRepository<FileReference, Guid> referenceRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _referenceRepository = Check.NotNull(referenceRepository);
    }

    public async Task<Result> ConfirmReferenceAsync(Guid fileId, string entityType, Guid entityId, string fieldName)
    {
        // 查找临时引用
        var temporaryRef = await _referenceRepository.FindAsync(r =>
            r.FileId == fileId &&
            r.EntityType == entityType &&
            r.EntityId == entityId &&
            r.FieldName == fieldName &&
            r.IsTemporary);

        if (temporaryRef != null)
        {
            // 将临时引用转为正式引用
            temporaryRef.IsTemporary = false;
            await _referenceRepository.UpdateAsync(temporaryRef);

            // 增加文件引用计数（原子 +1，见 FileReferenceCounts）
            await FileReferenceCounts.AddAsync(_repository, fileId);
        }
        else
        {
            // 如果没有临时引用，直接创建正式引用
            await CreateReferenceAsync(fileId, entityType, entityId, fieldName, isTemporary: false);
        }

        LogInformation("File reference confirmed: FileId: {FileId}, EntityType: {EntityType}, EntityId: {EntityId}, FieldName: {FieldName}", fileId, entityType, entityId, fieldName);
        return Ok("File reference confirmed");
    }

    public async Task<Result> UpdateReferenceAsync(Guid? oldFileId, Guid? newFileId, string entityType, Guid entityId, string fieldName)
    {
        // 删除旧引用
        if (oldFileId.HasValue)
        {
            var oldRefs = await _referenceRepository
                .ToListAsync(r => r.FileId == oldFileId.Value &&
                                 r.EntityType == entityType &&
                                 r.EntityId == entityId &&
                                 r.FieldName == fieldName);

            foreach (var oldRef in oldRefs)
            {
                await _referenceRepository.DeleteAsync(oldRef);

                // 减少旧文件的引用计数（原子 -1，不低于 0）
                await FileReferenceCounts.ReleaseAsync(_repository, oldFileId.Value);
            }
        }

        // 创建新引用
        if (newFileId.HasValue)
        {
            await CreateReferenceAsync(newFileId.Value, entityType, entityId, fieldName, isTemporary: false);
        }

        LogInformation("File reference updated: OldFileId: {OldFileId}, NewFileId: {NewFileId}, EntityType: {EntityType}, EntityId: {EntityId}, FieldName: {FieldName}", oldFileId?.ToString() ?? "null", newFileId?.ToString() ?? "null", entityType, entityId, fieldName);
        return Ok("File reference updated");
    }

    public async Task<Result> BatchConfirmReferencesAsync(IEnumerable<FileReferenceInfo> references, CancellationToken cancellationToken = default)
    {
        var referenceList = references.ToList();
        if (!referenceList.Any())
            return Ok("No references to confirm");

        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var confirmedCount = 0;
            foreach (var refInfo in referenceList)
            {
                // 查找临时引用
                var temporaryRef = await _referenceRepository.FindAsync(r =>
                    r.FileId == refInfo.FileId &&
                    r.EntityType == refInfo.EntityType &&
                    r.EntityId == refInfo.EntityId &&
                    r.FieldName == refInfo.FieldName &&
                    r.IsTemporary, cancellationToken);

                if (temporaryRef != null)
                {
                    temporaryRef.IsTemporary = false;
                    await _referenceRepository.UpdateAsync(temporaryRef, cancellationToken);

                    await FileReferenceCounts.AddAsync(_repository, refInfo.FileId, cancellationToken);
                    confirmedCount++;
                }
                else
                {
                    await CreateReferenceAsync(refInfo.FileId, refInfo.EntityType, refInfo.EntityId, refInfo.FieldName, isTemporary: false, cancellationToken);
                    confirmedCount++;
                }
            }
            LogInformation("Batch confirmed {Count} file references", confirmedCount);
            return Ok($"Batch confirmed {confirmedCount} file references");
        }, cancellationToken);
    }

    public async Task<Result> BatchUpdateReferencesAsync(string entityType, Guid entityId, Dictionary<string, IEnumerable<Guid>> fieldFileIds, CancellationToken cancellationToken = default)
    {
        if (fieldFileIds == null || !fieldFileIds.Any())
            return Ok("No references to update");

        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var updatedCount = 0;
            foreach (var kvp in fieldFileIds)
            {
                var fieldName = kvp.Key;
                var newFileIds = kvp.Value.ToList();

                // 获取该字段的旧引用
                var oldRefs = await _referenceRepository
                    .ToListAsync(r => r.EntityType == entityType &&
                                     r.EntityId == entityId &&
                                     r.FieldName == fieldName, cancellationToken);

                var oldFileIds = oldRefs.Select(r => r.FileId).ToList();

                // 找出需要删除的引用（旧有但新没有）
                var toDelete = oldFileIds.Except(newFileIds).ToList();
                foreach (var fileId in toDelete)
                {
                    var oldRef = oldRefs.FirstOrDefault(r => r.FileId == fileId);
                    if (oldRef != null)
                    {
                        await _referenceRepository.DeleteAsync(oldRef, cancellationToken);

                        await FileReferenceCounts.ReleaseAsync(_repository, fileId, cancellationToken);
                    }
                }

                // 找出需要添加的引用（新有但旧没有）
                var toAdd = newFileIds.Except(oldFileIds).ToList();
                foreach (var fileId in toAdd)
                {
                    await CreateReferenceAsync(fileId, entityType, entityId, fieldName, isTemporary: false, cancellationToken);
                }
                updatedCount++;
            }
            LogInformation("Batch updated references for entity: {EntityType}, {EntityId}, {Count} fields", entityType, entityId, updatedCount);
            return Ok($"Batch updated references for {updatedCount} fields");
        }, cancellationToken);
    }

    public async Task<Result<IEnumerable<FileReferenceDto>>> GetReferencesAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var references = await _referenceRepository.AsQueryable()
            .Where(r => r.FileId == fileId)
            .Select(ReferenceProjection)
            .ToListAsync(cancellationToken);

        return Ok((IEnumerable<FileReferenceDto>)references);
    }

    public async Task<Result<IEnumerable<FileReferenceDto>>> GetReferencesByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var references = await _referenceRepository.AsQueryable()
            .Where(r => r.EntityType == entityType && r.EntityId == entityId)
            .Select(ReferenceProjection)
            .ToListAsync(cancellationToken);

        return Ok((IEnumerable<FileReferenceDto>)references);
    }

    public async Task<Result<FileReferenceStatistics>> GetReferenceStatisticsAsync(string? entityType = null, CancellationToken cancellationToken = default)
    {
        var query = _referenceRepository.AsQueryable();

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(r => r.EntityType == entityType);
        }

        var totalReferences = await query.CountAsync(cancellationToken);
        var permanentReferences = await query.Where(r => !r.IsTemporary).CountAsync(cancellationToken);
        var temporaryReferences = await query.Where(r => r.IsTemporary).CountAsync(cancellationToken);

        var referencesByEntityType = await query
            .GroupBy(r => r.EntityType)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var statistics = new FileReferenceStatistics
        {
            TotalReferences = totalReferences,
            PermanentReferences = permanentReferences,
            TemporaryReferences = temporaryReferences,
            ReferencesByEntityType = referencesByEntityType.ToDictionary(x => x.EntityType, x => x.Count)
        };

        return Ok(statistics);
    }

    // ------------------------------------------------------------------
    // 引用计数的同步与校验
    //
    // ReferenceCount 混着两种引用：显式的 Storage_Reference 行，与**隐式**的持有者 ——
    // 正式上传（IsTemporary = false）从 1 起、MD5 复用每命中一次 +1，这两种都不产生引用行。
    // 所以「按引用行重算」只对临时记录成立：正式记录的隐式持有者数从引用行上根本读不出来，
    // 按引用行重算等于把每一条未绑定的正式上传与 MD5 共享文件归零，默认开启的孤儿回收随后
    // 物理删除它们（或任一持有者的 DeleteAsync 当场删掉别人仍在用的文件）。
    // 因此：同步只碰临时记录，正式记录一律不动；校验对正式记录只要求「不低于引用行数」——
    // 那是唯一能确定的不一致（解绑一次就会把还在用的文件删掉）。
    // ------------------------------------------------------------------

    private const string PermanentRecordCannotBeSynced =
        "Reference counts can only be rebuilt for temporary files. A permanent upload (or an MD5-reused file) carries implicit holders that are not recorded as reference rows, so recomputing it from reference rows would zero it and hand it to orphan cleanup.";

    public async Task<Result<int>> SyncReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<int>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (!fileRecord.IsTemporary)
            return Fail<int>(PermanentRecordCannotBeSynced, 400, ErrorCodes.VALIDATION_ERROR);

        var actualCount = await CountPermanentReferenceRowsAsync(fileId, cancellationToken);
        await FileReferenceCounts.SetAsync(_repository, fileId, actualCount, cancellationToken);

        return Ok(actualCount);
    }

    public async Task<Result<int>> SyncAllReferenceCountsAsync(CancellationToken cancellationToken = default)
    {
        var syncedCount = await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            // 只重算临时记录；正式记录（未绑定的正式上传 / MD5 共享文件）带隐式引用，
            // 从引用行上重建不出来，一律跳过。集合更新一条 SQL 完成，不把整张表拉进内存。
            var references = _referenceRepository.AsQueryable();
            return await _repository.AsQueryable()
                .Where(f => f.IsTemporary
                    && f.ReferenceCount != references.Count(r => r.FileId == f.Id && !r.IsTemporary))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(
                        f => f.ReferenceCount,
                        f => references.Count(r => r.FileId == f.Id && !r.IsTemporary)),
                    cancellationToken);
        }, cancellationToken);

        LogInformation("Synced reference counts for {Count} temporary files; permanent records were left untouched", syncedCount);
        return Ok(syncedCount);
    }

    public async Task<Result<bool>> ValidateReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<bool>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var actualCount = await CountPermanentReferenceRowsAsync(fileId, cancellationToken);

        // 临时记录：计数完全由引用行决定。正式记录：隐式持有者数未知，只有低于引用行数才是确定的不一致。
        var isValid = fileRecord.IsTemporary
            ? fileRecord.ReferenceCount == actualCount
            : fileRecord.ReferenceCount >= actualCount;
        return Ok(isValid);
    }

    private Task<int> CountPermanentReferenceRowsAsync(Guid fileId, CancellationToken cancellationToken)
        => _referenceRepository.AsQueryable()
            .CountAsync(r => r.FileId == fileId && !r.IsTemporary, cancellationToken);

    /// <summary>
    /// 手动清理最短的保留期。更年轻的临时上传可能属于一张还没提交的表单：上传与保存实体之间隔着用户填表的时间。
    /// </summary>
    internal static readonly TimeSpan MinimumManualCleanupAge = TimeSpan.FromHours(1);

    /// <summary>
    /// 删掉过了保留期、无人持有的临时文件（判据见 <see cref="TemporaryFileCriteria"/>），外加过期的临时引用行与它们指向的无人持有的记录。
    /// </summary>
    /// <remarks>
    /// ★★ 此前只遍历临时引用行，而真正的临时上传一条引用行都没有：维护页的「清理」恒报 0。
    /// 保留期不得短于 <see cref="MinimumManualCleanupAge"/>：0 或负数会让截止时刻落在现在或将来，
    /// 把用户刚刚上传、表单还没提交的文件一起删掉。
    /// </remarks>
    public async Task<Result<int>> CleanupTemporaryFilesAsync(TimeSpan? olderThan = null)
    {
        var age = olderThan ?? DefaultTemporaryAge;
        if (age < MinimumManualCleanupAge)
            return Fail<int>("Temporary files can only be cleaned up once they are at least one hour old; younger uploads may belong to a form that has not been submitted yet.", 400, ErrorCodes.VALIDATION_ERROR);

        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var cutoffTime = DateTime.UtcNow.Subtract(age);
            var deletedCount = 0;

            var expiredRecords = await _repository.AsQueryable()
                .Where(TemporaryFileCriteria.Expired(cutoffTime))
                .ToListAsync(cancellationToken);

            foreach (var fileRecord in expiredRecords)
            {
                // 条件删除：列出来之后才被绑定的（计数已不为 0）不删，理由见 FileReferenceCounts。
                // 指向它的临时引用行随记录一起走，下面按引用行那一趟就不会再找到它。
                if (!await FileReferenceCounts.TryDeleteUnheldAsync(_repository, _referenceRepository, fileRecord.Id, cancellationToken))
                    continue;

                await PublishDeletionAsync(fileRecord, cancellationToken);
                deletedCount++;
            }

            var temporaryRefs = await _referenceRepository
                .ToListAsync(r => r.IsTemporary && r.CreationTime < cutoffTime, cancellationToken);

            foreach (var reference in temporaryRefs)
            {
                var fileRecord = await _repository.AsQueryable().FirstOrDefaultAsync(f => f.Id == reference.FileId, cancellationToken);
                if (fileRecord != null
                    && IsUnheldOutsideAFolder(fileRecord)
                    && await FileReferenceCounts.TryDeleteUnheldAsync(_repository, _referenceRepository, fileRecord.Id, cancellationToken))
                {
                    await PublishDeletionAsync(fileRecord, cancellationToken);
                    deletedCount++;
                }

                // 按条件删：记录删掉时它名下的引用行已一并删掉，同一份文件的另一条临时引用行也可能刚被删过。
                await _referenceRepository.DeleteAsync(r => r.Id == reference.Id, cancellationToken);
            }

            LogInformation("Cleaned up {Count} temporary files", deletedCount);
            return Ok(deletedCount, $"Cleaned up {deletedCount} temporary files");
        });
    }

    /// <summary>
    /// 列出 <see cref="CleanupTemporaryFilesAsync"/> 会删的那些文件：同一个判据，所以列表里看到的正是清理会删的。
    /// </summary>
    public async Task<Result<IEnumerable<FileRecord>>> GetTemporaryFilesAsync(TimeSpan? olderThan = null)
    {
        var age = olderThan ?? DefaultTemporaryAge;
        if (age < TimeSpan.Zero)
            return Fail<IEnumerable<FileRecord>>("The age threshold cannot be negative.", 400, ErrorCodes.VALIDATION_ERROR);

        var cutoffTime = DateTime.UtcNow.Subtract(age);

        var expired = await _repository.AsQueryable()
            .Where(TemporaryFileCriteria.Expired(cutoffTime))
            .OrderBy(f => f.CreationTime)
            .ToListAsync();

        var fileIdsWithExpiredTemporaryRefs = await _referenceRepository
            .Where(r => r.IsTemporary && r.CreationTime < cutoffTime)
            .Select(r => r.FileId)
            .Distinct()
            .ToListAsync();

        var fromReferences = fileIdsWithExpiredTemporaryRefs.Count == 0
            ? []
            : (await _repository.GetListAsync(fileIdsWithExpiredTemporaryRefs)).Where(IsUnheldOutsideAFolder).ToList();

        var files = expired
            .Concat(fromReferences.Where(f => expired.All(e => e.Id != f.Id)))
            .ToList();

        return Ok((IEnumerable<FileRecord>)files);
    }

    /// <summary>无人持有，且不是归档进目录的临时记录（与孤儿回收同一条豁免）。</summary>
    private static bool IsUnheldOutsideAFolder(FileRecord fileRecord)
        => fileRecord.ReferenceCount <= 0 && !(fileRecord.IsTemporary && fileRecord.FolderId != null);

    /// <summary>管理端不给保留期时的默认值，与 <c>Storage:Cleanup:TemporaryFileRetentionHours</c> 的默认值一致。</summary>
    private static readonly TimeSpan DefaultTemporaryAge = TimeSpan.FromHours(24);

    /// <summary>
    /// FileReference -> FileReferenceDto 投影表达式（复用于多个查询方法）
    /// </summary>
    private static readonly Expression<Func<FileReference, FileReferenceDto>> ReferenceProjection = r => new FileReferenceDto
    {
        Id = r.Id,
        FileId = r.FileId,
        EntityType = r.EntityType,
        EntityId = r.EntityId,
        FieldName = r.FieldName,
        IsTemporary = r.IsTemporary,
        CreationTime = r.CreationTime
    };

    /// <summary>
    /// 创建文件引用
    /// </summary>
    private async Task CreateReferenceAsync(Guid fileId, string entityType, Guid entityId, string fieldName, bool isTemporary, CancellationToken cancellationToken = default)
    {
        // 去重：同一 (FileId, EntityType, EntityId, FieldName) 只应存在一条引用。
        // 避免手动引用 API 与自动追踪(FileReferenceProcessor)双轨写入产生重复行 + ReferenceCount 虚高，
        // 进而导致文件永远清不掉。与 FileReferenceProcessor 的去重语义保持一致。
        var existing = await _referenceRepository.FindAsync(r =>
            r.FileId == fileId &&
            r.EntityType == entityType &&
            r.EntityId == entityId &&
            r.FieldName == fieldName, cancellationToken);

        if (existing != null)
        {
            // 已存在引用：若现有为临时引用而本次需要正式引用，则转正并增加计数；
            // 其余情况(已是正式引用，或本次也是临时引用)保持不变，避免重复计数。
            if (!isTemporary && existing.IsTemporary)
            {
                existing.IsTemporary = false;
                await _referenceRepository.UpdateAsync(existing, cancellationToken);

                await FileReferenceCounts.AddAsync(_repository, fileId, cancellationToken);
            }
            return;
        }

        var reference = new FileReference
        {
            FileId = fileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName,
            IsTemporary = isTemporary
        };

        await _referenceRepository.InsertAsync(reference, cancellationToken);

        if (!isTemporary)
            await FileReferenceCounts.AddAsync(_repository, fileId, cancellationToken);
    }

    /// <summary>
    /// 记录删掉之后，请求删除它名下的对象，并通知别的程序集收掉挂在这个文件上的数据。
    /// </summary>
    /// <remarks>
    /// ★ 此前这条路径只删行：原件与缩略图留在存储里，而孤儿回收按记录枚举，永远找不到它们。
    /// 对象经 <see cref="FileDeleteRequestedEvent"/> 删除（事务感知：外层工作单元回滚则不删，处理器失败由总线重试），
    /// 与解绑归零那条路径同一个机制。没有事件总线时只能留下对象（只占空间），不能在提交之前就把它删掉。
    /// </remarks>
    private async Task PublishDeletionAsync(FileRecord fileRecord, CancellationToken cancellationToken)
    {
        if (EventBus == null)
        {
            LogWarning("No event bus: stored objects of deleted temporary file {FileId} are left in storage", fileRecord.Id);
            return;
        }

        await EventBus.PublishAsync(new FileDeleteRequestedEvent
        {
            FileId = fileRecord.Id,
            FilePath = fileRecord.Path,
            ThumbnailPath = fileRecord.ThumbnailPath,
            Provider = fileRecord.Provider ?? "Local"
        }, cancellationToken);
        await EventBus.PublishAsync(new FileRecordDeletedEvent { FileId = fileRecord.Id }, cancellationToken);
    }
}
