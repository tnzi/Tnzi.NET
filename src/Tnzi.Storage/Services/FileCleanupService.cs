namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理服务，负责临时文件、孤岛文件、无效引用的清理，并驱动别的程序集挂进来的 <see cref="IStorageCleanupContributor"/>。
///
/// 多租户隔离（T3）：
/// FileRecord/FileReference 均为 IMultiTenant。后台清理任务运行在
/// 无 HttpContext 的 scope 中，CurrentTenant 通常为空，若直接查询，框架在「启用多租户」时的全局
/// 租户过滤器（e.TenantId == CurrentTenant.Id）会因当前租户为 null 而放行/错配所有租户的数据，
/// 存在跨租户误删风险。
///
/// 处理方式（区分多租户是否启用，因为框架在「未启用」时会 Ignore 掉 TenantId 列，无法在 LINQ 中
/// 引用 TenantId）：
/// - 未启用多租户：TenantId 不是数据库列，全体数据属于单一逻辑租户，不存在跨租户问题，直接单遍清理。
/// - 启用多租户：先用 IgnoreQueryFilters() 跨租户取出待清理候选的 distinct TenantId，再对每个
///   TenantId 用 ICurrentTenant.Change(tenantId) 切换上下文，使框架的全局租户过滤器在该租户范围内
///   生效后再清理，从而严格按租户隔离，绝不跨租户删除他人文件。
/// </summary>
public class FileCleanupService : ApplicationService, IFileCleanupService
{
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IFileStorage _storage;
    private readonly ICurrentTenant _currentTenant;
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly bool _multiTenancyEnabled;
    private readonly IOrphanReferenceValidator? _orphanReferenceValidator;

    /// <summary>
    /// 别的程序集挂进来的清理趟次。<b>没有任何实现时 DI 给空集合</b>，整段跳过。
    /// </summary>
    private readonly IReadOnlyList<IStorageCleanupContributor> _contributors;

    /// <summary>
    /// 初始化 <see cref="FileCleanupService"/>
    /// </summary>
    public FileCleanupService(
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IFileStorage storage,
        ICurrentTenant currentTenant,
        IOptionsMonitor<StorageOptions> options,
        IServiceProvider serviceProvider,
        IEnumerable<IStorageCleanupContributor>? contributors = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        IOrphanReferenceValidator? orphanReferenceValidator = null)
        : base(serviceProvider)
    {
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _contributors = contributors?.ToArray() ?? [];
        _storage = Check.NotNull(storage);
        _currentTenant = Check.NotNull(currentTenant);
        _options = Check.NotNull(options);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _orphanReferenceValidator = orphanReferenceValidator;
    }

    /// <summary>
    /// 执行完整清理：临时文件、孤岛文件、无效引用，最后跑一遍贡献者
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
            if (_options.CurrentValue.Cleanup.EnableOrphanFileCleanup)
            {
                result.OrphanFilesDeleted = await CleanupOrphanFilesAsync(cancellationToken);
                LogInformation("Cleaned orphan files: {Count}", result.OrphanFilesDeleted);
            }

            // 3. 清理无效引用（实体已删除但引用仍在）
            if (_options.CurrentValue.Cleanup.EnableOrphanReferenceCleanup)
            {
                result.OrphanReferencesDeleted = await CleanupOrphanReferencesAsync(cancellationToken);
                LogInformation("Cleaned orphan references: {Count}", result.OrphanReferencesDeleted);
            }

            // 4. 别的程序集挂进来的趟次（工作区子模块的过期分片上传会话就在这里）
            result.ContributedDeleted = await RunContributorsAsync(result, cancellationToken);
            LogInformation("Cleaned by contributors: {Count}", result.ContributedDeleted);

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
    /// 清理过期临时文件（按租户隔离）
    /// </summary>
    public async Task<int> CleanupTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.CurrentValue.Cleanup.TemporaryFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.CurrentValue.Cleanup.MaxFilesPerRun;

        return await ForEachTenantAsync(
            // 跨租户取出待清理候选的 distinct TenantId（仅多租户启用时调用）
            () => _referenceRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(r => r.IsTemporary && r.CreationTime < cutoffTime)
                .Select(r => r.TenantId)
                .Distinct(),
            async () =>
            {
                // 在当前租户上下文内（全局过滤器已生效，无需显式 TenantId 条件——
                // 否则未启用多租户时 TenantId 列被 Ignore，LINQ 无法翻译）
                var temporaryRefs = await _referenceRepository.AsQueryable()
                    .Where(r => r.IsTemporary && r.CreationTime < cutoffTime)
                    .Take(maxFiles)
                    .ToListAsync(cancellationToken);

                var count = 0;
                foreach (var reference in temporaryRefs)
                {
                    try
                    {
                        var fileRecord = await _fileRepository.GetAsync(reference.FileId, cancellationToken);
                        if (fileRecord != null && fileRecord.ReferenceCount == 0)
                        {
                            await DeletePhysicalFileAsync(fileRecord);
                            await _fileRepository.DeleteAsync(fileRecord, cancellationToken);
                            count++;
                        }

                        await _referenceRepository.DeleteAsync(reference, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to clean temporary file: FileId={FileId}, Error={Error}", reference.FileId, ex.Message);
                    }
                }
                return count;
            },
            cancellationToken);
    }

    /// <summary>
    /// 清理孤岛文件（ReferenceCount=0 且超过保留期的文件，按租户隔离）
    /// </summary>
    public async Task<int> CleanupOrphanFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.CurrentValue.Cleanup.OrphanFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.CurrentValue.Cleanup.MaxFilesPerRun;

        return await ForEachTenantAsync(
            () => _fileRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(f => f.ReferenceCount <= 0 && f.CreationTime < cutoffTime)
                .Select(f => f.TenantId)
                .Distinct(),
            async () =>
            {
                var orphanFiles = await _fileRepository.AsQueryable()
                    .Where(f => f.ReferenceCount <= 0 && f.CreationTime < cutoffTime)
                    .Take(maxFiles)
                    .ToListAsync(cancellationToken);

                var count = 0;
                foreach (var fileRecord in orphanFiles)
                {
                    try
                    {
                        await DeletePhysicalFileAsync(fileRecord);
                        await _fileRepository.DeleteAsync(fileRecord, cancellationToken);
                        count++;

                        Logger.LogDebug("Cleaned orphan file: {FileId}", fileRecord.Id);
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to clean orphan file: FileId={FileId}, Error={Error}", fileRecord.Id, ex.Message);
                    }
                }
                return count;
            },
            cancellationToken);
    }

    /// <summary>
    /// 清理无效引用：实体已删除但引用记录仍在（按租户隔离）。
    /// 需实现 IOrphanReferenceValidator 校验实体是否存在
    /// </summary>
    public async Task<int> CleanupOrphanReferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_orphanReferenceValidator == null)
        {
            LogWarning("Orphan reference cleanup requires implementing IOrphanReferenceValidator");
            return 0;
        }

        var retention = TimeSpan.FromHours(_options.CurrentValue.Cleanup.OrphanFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.CurrentValue.Cleanup.MaxFilesPerRun;

        return await ForEachTenantAsync(
            () => _referenceRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(r => r.CreationTime < cutoffTime)
                .Select(r => r.TenantId)
                .Distinct(),
            async () =>
            {
                var references = await _referenceRepository.AsQueryable()
                    .Where(r => r.CreationTime < cutoffTime)
                    .Take(maxFiles)
                    .ToListAsync(cancellationToken);

                var count = 0;
                foreach (var reference in references)
                {
                    try
                    {
                        var exists = await _orphanReferenceValidator.IsEntityExistsAsync(
                            reference.EntityType,
                            reference.EntityId,
                            cancellationToken);

                        if (!exists)
                        {
                            await _referenceRepository.DeleteAsync(reference, cancellationToken);

                            // 递减文件的引用计数
                            var fileRecord = await _fileRepository.GetAsync(reference.FileId, cancellationToken);
                            if (fileRecord != null)
                            {
                                fileRecord.ReferenceCount = Math.Max(0, fileRecord.ReferenceCount - 1);
                                await _fileRepository.UpdateAsync(fileRecord, cancellationToken);
                            }

                            count++;
                            Logger.LogDebug("Cleaned orphan reference: {ReferenceId}, Entity={EntityType}/{EntityId}",
                                reference.Id, reference.EntityType, reference.EntityId);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to validate orphan reference: ReferenceId={ReferenceId}, Error={Error}", reference.Id, ex.Message);
                    }
                }
                return count;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RunContributorsAsync(CancellationToken cancellationToken = default)
        => RunContributorsAsync(result: null, cancellationToken);

    /// <summary>
    /// 逐个跑贡献者，把每个的失败**单独**记下来。
    /// </summary>
    /// <remarks>
    /// 一个贡献者抛异常不该把其它趟次和父模块自己的三趟一起带走 —— 那正是
    /// 「一个坏的贡献者让整次清理停摆」的形态。所以逐个 catch，记 Warning，
    /// 有结果对象时再往 <see cref="CleanupResult.Errors"/> 里追加一条。
    /// </remarks>
    private async Task<int> RunContributorsAsync(CleanupResult? result, CancellationToken cancellationToken)
    {
        if (_contributors.Count == 0)
            return 0;

        var maxItems = _options.CurrentValue.Cleanup.MaxFilesPerRun;
        var total = 0;

        foreach (var contributor in _contributors)
        {
            try
            {
                var deleted = await contributor.CleanupAsync(maxItems, cancellationToken);
                total += deleted;
                Logger.LogDebug("Cleanup contributor {Contributor} removed {Count} items", contributor.Name, deleted);
            }
            catch (Exception ex)
            {
                LogWarning("Cleanup contributor {Contributor} failed: {Error}", contributor.Name, ex.Message);
                result?.Errors.Add($"{contributor.Name}: {ex.Message}");
            }
        }

        return total;
    }

    /// <summary>
    /// 按租户隔离地执行清理动作（T3 核心）。
    /// - 未启用多租户：TenantId 列被框架 Ignore，无法按租户分组；全体属于单一逻辑租户，直接执行一次。
    /// - 启用多租户：用 <paramref name="tenantIdQuery"/>（IgnoreQueryFilters 跨租户）取出 distinct TenantId，
    ///   逐个 ICurrentTenant.Change(t) 切换上下文后执行 <paramref name="perTenantAction"/>，
    ///   使全局租户过滤器在该租户范围内生效，确保不跨租户误删。
    /// </summary>
    private async Task<int> ForEachTenantAsync(
        Func<IQueryable<Guid?>> tenantIdQuery,
        Func<Task<int>> perTenantAction,
        CancellationToken cancellationToken)
    {
        if (!_multiTenancyEnabled)
        {
            // 单一逻辑租户，直接执行
            return await perTenantAction();
        }

        var tenantIds = await tenantIdQuery().ToListAsync(cancellationToken);

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            using (_currentTenant.Change(tenantId))
            {
                total += await perTenantAction();
            }
        }
        return total;
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
