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
    private readonly OrphanReferenceScanCursor _scanCursor;

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
        OrphanReferenceScanCursor scanCursor,
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
        // 必需而非可选：游标要跨轮次活着（模块注册成单例），每次构造一份新的就退回「每轮查同一批」。
        _scanCursor = Check.NotNull(scanCursor);
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
    /// 清理过期临时文件（按租户隔离）：过了 <c>TemporaryFileRetentionHours</c> 仍无人持有的临时记录，
    /// 外加过期的临时引用行。
    /// </summary>
    /// <remarks>
    /// ★★ 判据见 <see cref="TemporaryFileCriteria"/>：临时文件是<b>记录</b>，此前这一趟只遍历临时引用行，
    /// 真正的临时上传（一条引用行都没有）要等孤儿回收的保留期才被收走，孤儿回收关掉时永远不收。
    /// 临时引用行那一趟保留：它管的是调用方经 <c>SaveWithReferenceAsync(isTemporary: true)</c> 显式写下的临时持有。
    /// </remarks>
    public async Task<int> CleanupTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.CurrentValue.Cleanup.TemporaryFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.CurrentValue.Cleanup.MaxFilesPerRun;

        var records = await CleanupExpiredTemporaryRecordsAsync(cutoffTime, maxFiles, cancellationToken);
        var byReference = await CleanupExpiredTemporaryReferencesAsync(cutoffTime, maxFiles, cancellationToken);
        return records + byReference;
    }

    /// <summary>
    /// 过了保留期、计数为 0、没归档进目录的临时记录：连对象带记录一起删。
    /// </summary>
    private async Task<int> CleanupExpiredTemporaryRecordsAsync(DateTime cutoffTime, int maxFiles, CancellationToken cancellationToken)
    {
        var isExpired = TemporaryFileCriteria.Expired(cutoffTime);

        return await ForEachTenantAsync(
            () => _fileRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(isExpired)
                .Select(f => f.TenantId)
                .Distinct(),
            async () =>
            {
                var expired = await _fileRepository.AsQueryable()
                    .Where(isExpired)
                    .OrderBy(f => f.CreationTime)
                    .Take(maxFiles)
                    .ToListAsync(cancellationToken);

                var count = 0;
                foreach (var fileRecord in expired)
                {
                    try
                    {
                        // 先条件删记录、删掉了才删对象（理由见 FileReferenceCounts）：列出来之后才被绑定的，一个字节都不碰。
                        // 指向它的临时引用行随记录一起走，否则下一趟按引用行再找它一次。
                        if (!await FileReferenceCounts.TryDeleteUnheldAsync(_fileRepository, _referenceRepository, fileRecord.Id, cancellationToken))
                            continue;

                        await DeletePhysicalFileAsync(fileRecord, cancellationToken);
                        await PublishFileRecordDeletedAsync(fileRecord.Id, cancellationToken);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to clean temporary file: FileId={FileId}, Error={Error}", fileRecord.Id, ex.Message);
                    }
                }
                return count;
            },
            cancellationToken);
    }

    /// <summary>
    /// 过期的临时引用行：删掉引用行，它指向的记录若无人持有，一并删掉。
    /// </summary>
    private async Task<int> CleanupExpiredTemporaryReferencesAsync(DateTime cutoffTime, int maxFiles, CancellationToken cancellationToken)
    {
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
                        var fileRecord = await _fileRepository.AsQueryable().FirstOrDefaultAsync(f => f.Id == reference.FileId, cancellationToken);
                        if (fileRecord != null
                            && fileRecord.ReferenceCount <= 0
                            && !(fileRecord.IsTemporary && fileRecord.FolderId != null)
                            && await FileReferenceCounts.TryDeleteUnheldAsync(_fileRepository, _referenceRepository, fileRecord.Id, cancellationToken))
                        {
                            await DeletePhysicalFileAsync(fileRecord, cancellationToken);
                            await PublishFileRecordDeletedAsync(fileRecord.Id, cancellationToken);
                            count++;
                        }

                        // 按条件删：记录删掉时它名下的引用行已一并删掉，同一份文件的另一条临时引用行也可能刚被删过。
                        await _referenceRepository.DeleteAsync(r => r.Id == reference.Id, cancellationToken);
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
    /// 清理孤岛文件（ReferenceCount=0 且超过保留期的文件，按租户隔离；归档进目录的临时记录除外）
    /// </summary>
    public async Task<int> CleanupOrphanFilesAsync(CancellationToken cancellationToken = default)
    {
        var retention = TimeSpan.FromHours(_options.CurrentValue.Cleanup.OrphanFileRetentionHours);
        var cutoffTime = DateTime.UtcNow.Subtract(retention);
        var maxFiles = _options.CurrentValue.Cleanup.MaxFilesPerRun;

        // ★ 归档进目录的临时记录不是孤儿（FolderId 由工作区子模块写入，列在本表）。
        //   这类行来自「文件库直传落临时记录、挪进目录只改 FolderId」那段时期：没有任何引用行、计数为 0，
        //   回收它等于删掉用户整理在文件库里的文件，分享链接 404、版本行成孤儿，且没有任何告警。
        //   挪动现在会把临时记录转正，这里守的是之前已经躺在目录里的那些行。
        //   代价是这类行里被用户删掉、而物理删除又恰好失败的那一份不会再被本趟重试（保留，不误删）。
        Expression<Func<FileRecord, bool>> isOrphan = f => f.ReferenceCount <= 0
            && f.CreationTime < cutoffTime
            && !(f.IsTemporary && f.FolderId != null);

        return await ForEachTenantAsync(
            () => _fileRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(isOrphan)
                .Select(f => f.TenantId)
                .Distinct(),
            async () =>
            {
                var orphanFiles = await _fileRepository.AsQueryable()
                    .Where(isOrphan)
                    .Take(maxFiles)
                    .ToListAsync(cancellationToken);

                var count = 0;
                foreach (var fileRecord in orphanFiles)
                {
                    try
                    {
                        // ★ 候选是一会儿之前读出来的快照：期间一次 MD5 复用命中、或一次实体绑定可能刚把它重新持有。
                        //   此前照快照删除，用户刚拿到的那条记录随即连对象一起没了。现在先在库里复核并删掉记录
                        //   （计数仍 <= 0、没有正式引用行），删掉了才删对象。
                        if (!await FileReferenceCounts.TryDeleteUnheldAsync(_fileRepository, _referenceRepository, fileRecord.Id, cancellationToken))
                            continue;

                        await DeletePhysicalFileAsync(fileRecord, cancellationToken);
                        await PublishFileRecordDeletedAsync(fileRecord.Id, cancellationToken);
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
                var references = await NextOrphanReferencePageAsync(cutoffTime, maxFiles, cancellationToken);

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

                            // 递减文件的引用计数（原子 -1，不低于 0）
                            await FileReferenceCounts.ReleaseAsync(_fileRepository, reference.FileId, cancellationToken);

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

    /// <summary>
    /// 取当前租户这一轮要查的那一页引用：从 <see cref="OrphanReferenceScanCursor"/> 记下的位置按 id 接着往后取，
    /// 取到末尾回到开头。
    /// </summary>
    /// <remarks>
    /// 本进程里第一次扫这个租户时，从一个随机位置开始（理由见 <see cref="OrphanReferenceScanCursor"/>）。
    /// 这一页不满说明到了末尾，下一轮从头开始；这一页是空的而起点不在开头，说明起点之后已经没有了，当场从头再取一次，
    /// 免得白跑一轮。
    /// </remarks>
    private async Task<List<FileReference>> NextOrphanReferencePageAsync(DateTime cutoffTime, int maxFiles, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.Id;
        var candidates = _referenceRepository.AsQueryable().Where(r => r.CreationTime < cutoffTime);

        if (!_scanCursor.TryGet(tenantId, out var position))
        {
            var total = await candidates.CountAsync(cancellationToken);
            var start = total > maxFiles
                ? await candidates.OrderBy(r => r.Id).Skip(Random.Shared.Next(total)).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken)
                : null;
            position = new OrphanReferenceScanCursor.Position(start);
        }

        var page = await PageAfterAsync(candidates, position.After, maxFiles, cancellationToken);
        if (page.Count == 0 && position.After != null)
            page = await PageAfterAsync(candidates, after: null, maxFiles, cancellationToken);

        _scanCursor.Set(tenantId, new OrphanReferenceScanCursor.Position(page.Count < maxFiles ? null : page[^1].Id));
        return page;
    }

    private static Task<List<FileReference>> PageAfterAsync(IQueryable<FileReference> candidates, Guid? after, int maxFiles, CancellationToken cancellationToken)
    {
        if (after is { } cursor)
            candidates = candidates.Where(r => r.Id.CompareTo(cursor) > 0);

        return candidates.OrderBy(r => r.Id).Take(maxFiles).ToListAsync(cancellationToken);
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
    /// 记录已删除：通知别的程序集收掉挂在这个文件上的数据（工作区的历史版本与分享链接）。
    /// </summary>
    private async Task PublishFileRecordDeletedAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new FileRecordDeletedEvent { FileId = fileId }, cancellationToken);
    }

    /// <summary>
    /// 删除一条<b>已经删掉记录</b>的文件的物理对象（正文与缩略图）；删不掉的交给删除事件重试（见 <see cref="DeletedFileObjects"/>）。
    /// </summary>
    private Task DeletePhysicalFileAsync(FileRecord fileRecord, CancellationToken cancellationToken)
        => DeletedFileObjects.DisposeAsync(_storage, EventBus, Logger, fileRecord, cancellationToken);
}
