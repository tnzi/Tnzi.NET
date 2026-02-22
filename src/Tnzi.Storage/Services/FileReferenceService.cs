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

            // 增加文件引用计数
            var fileRecord = await _repository.FindAsync(fileId);
            if (fileRecord != null)
            {
                fileRecord.ReferenceCount++;
                await _repository.UpdateAsync(fileRecord);
            }
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

                // 减少旧文件的引用计数
                var oldFile = await _repository.GetAsync(oldFileId.Value);
                if (oldFile != null)
                {
                    DecrementReferenceCount(oldFile);
                    await _repository.UpdateAsync(oldFile);
                }
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

                    var fileRecord = await _repository.GetAsync(refInfo.FileId, cancellationToken);
                    if (fileRecord != null)
                    {
                        fileRecord.ReferenceCount++;
                        await _repository.UpdateAsync(fileRecord, cancellationToken);
                    }
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

                        var oldFile = await _repository.GetAsync(fileId, cancellationToken);
                        if (oldFile != null)
                        {
                            DecrementReferenceCount(oldFile);
                            await _repository.UpdateAsync(oldFile, cancellationToken);
                        }
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

    public async Task<Result<int>> SyncReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var actualCount = await _referenceRepository.AsQueryable()
            .CountAsync(r => r.FileId == fileId && !r.IsTemporary, cancellationToken);

        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<int>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        fileRecord.ReferenceCount = actualCount;
        await _repository.UpdateAsync(fileRecord, cancellationToken);

        return Ok(actualCount);
    }

    public async Task<Result<int>> SyncAllReferenceCountsAsync(CancellationToken cancellationToken = default)
    {
        var syncedCount = await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            // 一次查询获取所有文件的实际引用计数
            var referenceCounts = await _referenceRepository.AsQueryable()
                .Where(r => !r.IsTemporary)
                .GroupBy(r => r.FileId)
                .Select(g => new { FileId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var countDict = referenceCounts.ToDictionary(x => x.FileId, x => x.Count);

            // 一次查询加载所有文件记录
            var allFiles = await _repository.ToListAsync(cancellationToken: cancellationToken);

            // 筛选出引用计数不匹配的记录
            var mismatchedFiles = allFiles.Where(f =>
            {
                var actualCount = countDict.GetValueOrDefault(f.Id, 0);
                return f.ReferenceCount != actualCount;
            }).ToList();

            // 批量更新
            foreach (var file in mismatchedFiles)
            {
                file.ReferenceCount = countDict.GetValueOrDefault(file.Id, 0);
            }

            if (mismatchedFiles.Count > 0)
            {
                await _repository.UpdateManyAsync(mismatchedFiles, cancellationToken);
            }

            return mismatchedFiles.Count;
        }, cancellationToken);

        LogInformation("Synced reference counts for {Count} files", syncedCount);
        return Ok(syncedCount);
    }

    public async Task<Result<bool>> ValidateReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var actualCount = await _referenceRepository.AsQueryable()
            .CountAsync(r => r.FileId == fileId && !r.IsTemporary, cancellationToken);

        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<bool>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var isValid = fileRecord.ReferenceCount == actualCount;
        return Ok(isValid);
    }

    public async Task<Result<int>> CleanupTemporaryFilesAsync(TimeSpan? olderThan = null)
    {
        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var cutoffTime = DateTime.UtcNow.Subtract(olderThan ?? TimeSpan.FromHours(24));

            var temporaryRefs = await _referenceRepository
                .ToListAsync(r => r.IsTemporary && r.CreationTime < cutoffTime, cancellationToken);

            var deletedCount = 0;
            foreach (var reference in temporaryRefs)
            {
                var fileRecord = await _repository.GetAsync(reference.FileId, cancellationToken);
                if (fileRecord != null && fileRecord.ReferenceCount == 0)
                {
                    await _repository.DeleteAsync(fileRecord, cancellationToken);
                    deletedCount++;
                }

                await _referenceRepository.DeleteAsync(reference, cancellationToken);
            }

            LogInformation("Cleaned up {Count} temporary files", deletedCount);
            return Ok(deletedCount, $"Cleaned up {deletedCount} temporary files");
        });
    }

    public async Task<Result<IEnumerable<FileRecord>>> GetTemporaryFilesAsync(TimeSpan? olderThan = null)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(olderThan ?? TimeSpan.FromHours(24));

        var temporaryFileIds = await _referenceRepository
            .Where(r => r.IsTemporary && r.CreationTime < cutoffTime)
            .Select(r => r.FileId)
            .Distinct()
            .ToListAsync();

        var files = await _repository.GetListAsync(temporaryFileIds);

        return Ok((IEnumerable<FileRecord>)files);
    }

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
        var reference = new FileReference
        {
            FileId = fileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName,
            IsTemporary = isTemporary,
            CreationTime = DateTime.UtcNow
        };

        await _referenceRepository.InsertAsync(reference, cancellationToken);

        if (!isTemporary)
        {
            var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
            if (fileRecord != null)
            {
                fileRecord.ReferenceCount++;
                await _repository.UpdateAsync(fileRecord, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 安全地减少文件引用计数（防止变为负数）
    /// </summary>
    private static int DecrementReferenceCount(FileRecord fileRecord)
    {
        fileRecord.ReferenceCount = Math.Max(0, fileRecord.ReferenceCount - 1);
        return fileRecord.ReferenceCount;
    }
}
