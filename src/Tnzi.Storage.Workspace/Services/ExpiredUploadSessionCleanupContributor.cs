namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 把「过期的分片上传会话 + 它残留的分片（含物理文件）」这一趟挂进父模块的后台清理。
/// </summary>
/// <remarks>
/// <para>
/// 这段逻辑原先写死在父模块的 <c>FileCleanupService</c> 里，靠直接注入
/// <c>IRepository&lt;FileUploadSession&gt;</c> 与 <c>IRepository&lt;FileChunk&gt;</c> 工作。
/// 两张表随本模块搬走之后，那两个构造参数会在没加载本模块的宿主上解析失败 ——
/// 而背后的托管服务是<b>无条件注册</b>的，于是表现是启动即崩，不是少一项能力。
/// 现在改成经 <see cref="IStorageCleanupContributor"/> 挂进去：没有本模块，就只是这一趟不跑。
/// </para>
/// <para>
/// <b>租户隔离逐字沿用原实现</b>：清理跑在没有 <c>HttpContext</c> 的作用域里，当前租户通常为空，
/// 而启用多租户时的全局过滤器（<c>e.TenantId == CurrentTenant.Id</c>）会因此放行所有租户的数据。
/// 所以启用多租户时先用 <c>IgnoreQueryFilters()</c> 跨租户取出待清理候选的 distinct
/// <c>TenantId</c>，再逐个 <c>ICurrentTenant.Change</c> 切上下文后清理。未启用多租户时
/// <c>TenantId</c> 根本不是数据库列（框架会 Ignore 掉），LINQ 里引用不了，直接单遍清理。
/// </para>
/// <para>
/// ★ 单次上限由调用方给（父模块的 <c>Storage:Cleanup:MaxFilesPerRun</c>），本类不自己读配置：
/// 「一趟最多处理多少条」是清理任务的排程策略，不该在每个贡献者里各定一份。
/// </para>
/// </remarks>
public class ExpiredUploadSessionCleanupContributor : IStorageCleanupContributor
{
    private readonly IRepository<FileUploadSession, Guid> _sessionRepository;
    private readonly IRepository<FileChunk, Guid> _chunkRepository;
    private readonly IFileStorage _storage;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<ExpiredUploadSessionCleanupContributor> _logger;
    private readonly bool _multiTenancyEnabled;

    public ExpiredUploadSessionCleanupContributor(
        IRepository<FileUploadSession, Guid> sessionRepository,
        IRepository<FileChunk, Guid> chunkRepository,
        IFileStorage storage,
        ICurrentTenant currentTenant,
        ILogger<ExpiredUploadSessionCleanupContributor> logger,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _sessionRepository = Check.NotNull(sessionRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _storage = Check.NotNull(storage);
        _currentTenant = Check.NotNull(currentTenant);
        _logger = Check.NotNull(logger);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <inheritdoc />
    public string Name => "ExpiredUploadSessions";

    /// <inheritdoc />
    public async Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (!_multiTenancyEnabled)
        {
            return await CleanupTenantScopeAsync(now, maxItems, cancellationToken);
        }

        var tenantIds = await _sessionRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(s => s.ExpiresAt < now)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            using (_currentTenant.Change(tenantId))
            {
                total += await CleanupTenantScopeAsync(now, maxItems, cancellationToken);
            }
        }
        return total;
    }

    /// <summary>
    /// 在<b>当前</b>租户上下文里清一趟（全局过滤器已经把范围收好了，这里不再写 TenantId 条件）。
    /// </summary>
    private async Task<int> CleanupTenantScopeAsync(DateTime now, int maxItems, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.AsQueryable()
            .Where(s => s.ExpiresAt < now)
            .Take(maxItems)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var session in sessions)
        {
            try
            {
                var chunks = await _chunkRepository.AsQueryable()
                    .Where(c => c.UploadSessionId == session.Id)
                    .ToListAsync(cancellationToken);

                // 物理分片先删。删不掉只告警不中断：残留的是一个孤立对象，
                // 而中断会把这条会话记录一起留下，下一趟再来一次同样删不掉。
                foreach (var chunk in chunks)
                {
                    if (string.IsNullOrEmpty(chunk.ChunkPath))
                        continue;

                    try
                    {
                        await _storage.DeleteAsync(chunk.ChunkPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to delete chunk file: {Path}, Error={Error}", chunk.ChunkPath, ex.Message);
                    }
                }

                if (chunks.Count > 0)
                {
                    await _chunkRepository.DeleteManyAsync(chunks, cancellationToken);
                }

                await _sessionRepository.DeleteAsync(session, cancellationToken);
                count++;

                _logger.LogDebug("Cleaned expired upload session: {SessionId}", session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to clean expired upload session: SessionId={SessionId}, Error={Error}", session.Id, ex.Message);
            }
        }

        return count;
    }
}
