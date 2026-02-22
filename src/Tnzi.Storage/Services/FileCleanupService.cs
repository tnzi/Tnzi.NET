namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理服务，负责临时文件、孤岛文件和无效引用的清理
/// </summary>
public class FileCleanupService : ApplicationService, IFileCleanupService
{
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IFileStorage _storage;
    private readonly StorageOptions _options;
    private readonly IOrphanReferenceValidator? _orphanReferenceValidator;

    /// <summary>
    /// 初始化 <see cref="FileCleanupService"/>
    /// </summary>
    public FileCleanupService(
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IFileStorage storage,
        IOptions<StorageOptions> options,
        IServiceProvider serviceProvider,
        IOrphanReferenceValidator? orphanReferenceValidator = null)
        : base(serviceProvider)
    {
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _storage = Check.NotNull(storage);
        _options = Check.NotNull(options).Value;
        _orphanReferenceValidator = orphanReferenceValidator;
    }

    /// <summary>
    /// 执行完整清理：临时文件、孤岛文件、无效引用
    /// </summary>
    public async Task<CleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var result = new CleanupResult();

        LogInformation("Start file cleanup task");

        try
        {
            // 1. 清理临时文件
            result.TemporaryFilesDeleted = await CleanupTemporaryFilesAsync(cancellationToken);
            LogInformation("Cleaned temporary files: {Count}", result.TemporaryFilesDeleted);

            // 2. 清理孤岛文件
            if (_options.Cleanup.EnableOrphanFileCleanup)
            {
                result.OrphanFilesDeleted = await CleanupOrphanFilesAsync(cancellationToken);
                LogInformation("Cleaned orphan files: {Count}", result.OrphanFilesDeleted);
            }

            // 3. 清理无效引用（实体已删除但引用仍在）
            if (_options.Cleanup.EnableOrphanReferenceCleanup)
            {
                result.OrphanReferencesDeleted = await CleanupOrphanReferencesAsync(cancellationToken);
                LogInformation("Cleaned orphan references: {Count}", result.OrphanReferencesDeleted);
            }

            LogInformation("File cleanup task completed, total cleaned: {Total}", result.TotalDeleted);
        }
        catch (Exception ex)
        {
            LogError("File cleanup task failed: {Message}", ex.Message);
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// 清理过期临时文件
    /// </summary>
    public async Task<int> CleanupTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.Cleanup.TemporaryFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.Cleanup.MaxFilesPerRun;

        // 查询过期临时引用
        var temporaryRefs = await _referenceRepository.AsQueryable()
            .Where(r => r.IsTemporary && r.CreationTime < cutoffTime)
            .Take(maxFiles)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;

        foreach (var reference in temporaryRefs)
        {
            try
            {
                var fileRecord = await _fileRepository.GetAsync(reference.FileId, cancellationToken);
                if (fileRecord != null && fileRecord.ReferenceCount == 0)
                {
                    // 删除物理文件
                    await DeletePhysicalFileAsync(fileRecord);
                    // 删除记录
                    await _fileRepository.DeleteAsync(fileRecord, cancellationToken);
                    deletedCount++;
                }

                // 删除引用
                await _referenceRepository.DeleteAsync(reference, cancellationToken);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to clean temporary file: FileId={FileId}, Error={Error}", reference.FileId, ex.Message);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// 清理孤岛文件（ReferenceCount=0 且超过保留期的文件）
    /// </summary>
    public async Task<int> CleanupOrphanFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.Cleanup.OrphanFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.Cleanup.MaxFilesPerRun;

        // 查询 ReferenceCount <= 0 且超过保留期的文件
        var orphanFiles = await _fileRepository.AsQueryable()
            .Where(f => f.ReferenceCount <= 0 && f.CreationTime < cutoffTime)
            .Take(maxFiles)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;

        foreach (var fileRecord in orphanFiles)
        {
            try
            {
                // 删除物理文件
                await DeletePhysicalFileAsync(fileRecord);
                // 删除记录
                await _fileRepository.DeleteAsync(fileRecord, cancellationToken);
                deletedCount++;

                Logger.LogDebug("Cleaned orphan file: {FileId}", fileRecord.Id);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to clean orphan file: FileId={FileId}, Error={Error}", fileRecord.Id, ex.Message);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// 清理无效引用：实体已删除但引用记录仍在。
    /// 需实现 IOrphanReferenceValidator 校验实体是否存在
    /// </summary>
    public async Task<int> CleanupOrphanReferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_orphanReferenceValidator == null)
        {
            LogWarning("Orphan reference cleanup requires implementing IOrphanReferenceValidator");
            return 0;
        }

        var retention = TimeSpan.FromHours(_options.Cleanup.OrphanFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.Cleanup.MaxFilesPerRun;

        // 查询超过保留期的引用
        var references = await _referenceRepository.AsQueryable()
            .Where(r => r.CreationTime < cutoffTime)
            .Take(maxFiles)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;

        foreach (var reference in references)
        {
            try
            {
                // 校验关联实体是否仍存在
                var exists = await _orphanReferenceValidator.IsEntityExistsAsync(
                    reference.EntityType,
                    reference.EntityId,
                    cancellationToken);

                if (!exists)
                {
                    // 删除无效引用
                    await _referenceRepository.DeleteAsync(reference, cancellationToken);

                    // 递减文件的引用计数
                    var fileRecord = await _fileRepository.GetAsync(reference.FileId, cancellationToken);
                    if (fileRecord != null)
                    {
                        fileRecord.ReferenceCount = Math.Max(0, fileRecord.ReferenceCount - 1);
                        await _fileRepository.UpdateAsync(fileRecord, cancellationToken);
                    }

                    deletedCount++;
                    Logger.LogDebug("Cleaned orphan reference: {ReferenceId}, Entity={EntityType}/{EntityId}",
                        reference.Id, reference.EntityType, reference.EntityId);
                }
            }
            catch (Exception ex)
            {
                LogWarning("Failed to validate orphan reference: ReferenceId={ReferenceId}, Error={Error}", reference.Id, ex.Message);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// 删除物理文件及其缩略图
    /// </summary>
    private async Task DeletePhysicalFileAsync(FileRecord fileRecord)
    {
        // 删除主文件
        if (!string.IsNullOrEmpty(fileRecord.Path))
        {
            try
            {
                await _storage.DeleteAsync(fileRecord.Path);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to delete physical file: {Path}, Error={Error}", fileRecord.Path, ex.Message);
            }
        }

        // 删除缩略图
        if (!string.IsNullOrEmpty(fileRecord.ThumbnailPath))
        {
            try
            {
                await _storage.DeleteAsync(fileRecord.ThumbnailPath);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to delete thumbnail: {Path}, Error={Error}", fileRecord.ThumbnailPath, ex.Message);
            }
        }
    }
}
