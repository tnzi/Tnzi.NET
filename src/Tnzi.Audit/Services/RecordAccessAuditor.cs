using System.Security.Cryptography;
using CoreErrorCodes = Tnzi.Exceptions.ErrorCodes;

namespace Tnzi.Audit.Services;

/// <summary>
/// <see cref="IRecordAccessAuditor"/> 的默认实现：按用户维护防篡改哈希链，并施加读取配额。
/// </summary>
/// <remarks>
/// <para>
/// <strong>链按用户分。</strong>全局单链会让每次审计写入都争抢同一个链尾，
/// 而审计写入位于读取热路径上。按用户分链把冲突面缩到「同一个人同时发起多次读取」，
/// 代价是无法直接比较两个用户之间的先后顺序（那不是审计链要回答的问题）。
/// </para>
/// <para>
/// <strong>并发靠唯一索引而不是锁。</strong>两个请求同时读到同一个链尾时，
/// 都会算出同一个 <c>Sequence</c>，数据库的 <c>(UserId, Sequence)</c> 唯一索引拒绝其一，
/// 服务层重读链尾后重试。这比引入分布式锁更适合高频路径。
/// </para>
/// </remarks>
public class RecordAccessAuditor : ApplicationService, IRecordAccessAuditor
{
    private readonly IRepository<AuditRecordAccess, Guid> _repository;
    private readonly IOptionsMonitor<RecordAccessAuditOptions> _options;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;

    /// <summary>
    /// 初始化 <see cref="RecordAccessAuditor"/>。
    /// </summary>
    /// <param name="repository">记录级读审计仓储。</param>
    /// <param name="options">记录级读审计选项（热读）。</param>
    /// <param name="serviceProvider">服务提供程序。</param>
    /// <param name="currentTenant">当前租户访问器；查询面按调用者的租户收口（见 <see cref="CallerTenantScope"/>）。</param>
    /// <param name="multiTenancyOptions">多租户开关；关闭时 <c>TenantId</c> 列不存在，谓词一行不加。</param>
    public RecordAccessAuditor(
        IRepository<AuditRecordAccess, Guid> repository,
        IOptionsMonitor<RecordAccessAuditOptions> options,
        IServiceProvider serviceProvider,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
        _currentTenant = currentTenant;
        _multiTenancyOptions = multiTenancyOptions;
    }

    /// <summary>调用者的租户作用域（每次现算）。</summary>
    private CallerTenantScope TenantScope => CallerTenantScope.Resolve(_multiTenancyOptions, _currentTenant, CurrentUser);

    /// <summary>
    /// 查询面的起点：调用者有租户则钉在本租户，宿主看全部。
    /// 写入面（登记 / 配额 / 链尾）刻意不走这里 —— 那三处按用户定位，且不该被任何过滤器挡住。
    /// </summary>
    private IQueryable<AuditRecordAccess> ScopedAccesses()
    {
        var scope = TenantScope;
        if (!scope.IsPinned)
        {
            return _repository.AsQueryable();
        }

        var tenantId = scope.TenantId;
        return _repository.Where(e => e.TenantId == tenantId);
    }

    /// <inheritdoc />
    public async Task<Result> RecordAsync(
        string resourceType,
        string resourceId,
        string? purpose = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(resourceType);
        Check.NotNullOrWhiteSpace(resourceId);

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            // 未启用即空操作，调用方无需判断开关。
            return Ok();
        }

        var userId = CurrentUser?.Id;
        var userName = CurrentUser?.UserName;

        var quota = await CheckQuotaAsync(userId, options, cancellationToken);
        if (!quota.Succeeded)
        {
            return quota;
        }

        var attempts = Math.Max(1, options.MaxWriteRetries);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var tail = await LoadChainTailAsync(userId, cancellationToken);

            var entry = new AuditRecordAccess
            {
                Sequence = (tail?.Sequence ?? 0) + 1,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Purpose = purpose,
                UserId = userId,
                UserName = userName,
                // 与 AuditOperation 同口径：这两张表都不实现 IMultiTenant（审计写入不该被
                // 租户过滤器挡住），所以租户归属要手工带上——不带的话，多租户部署下
                // 「上个月谁看过这位举报人的材料」就没法按租户回答，配置里建的那条租户索引
                // 也永远扫不到值。
                TenantId = CurrentUser?.TenantId,
                PreviousHash = tail?.Hash ?? string.Empty,
                CreationTime = DateTime.UtcNow
            };
            entry.Hash = ComputeHash(entry);

            try
            {
                await _repository.InsertAsync(entry, cancellationToken);

                // ★ 显式 flush，而不是把 SaveChanges 留给提交。事务启用时仓储会推迟保存，于是：
                //   ① 同一工作单元里第二次登记读到的链尾还是库里那条，两条算出同一个 Sequence；
                //   ② 唯一索引在提交时才拒绝，那时本循环早已退出，整个业务操作 500 而不是重试。
                //   flush 经工作单元进行，仍在调用方事务内（回滚照样撤掉）；EF 在显式事务内保存前
                //   自动建保存点，冲突失败后回滚到保存点，事务本身仍可继续用。未启用事务时
                //   InsertAsync 已经落库，这一步是空操作。
                await _repository.SaveChangesAsync(cancellationToken);
                return Ok();
            }
            catch (Exception ex) when (IsConcurrencyConflict(ex))
            {
                // 失败的实体仍以 Added 留在变更跟踪器里：不丢弃，它会被下一次 SaveChanges（重试那次，
                // 或本作用域里任何一次无关的保存）重放、再撞一次同一条索引 —— 重试循环因此从未成功过。
                _repository.Discard(entry);

                if (attempt >= attempts)
                {
                    break;
                }

                // 另一个请求抢到了同一个序号：重读链尾后重试。
                LogInformation(
                    "Record access audit chain conflict for user {UserId} at sequence {Sequence}, retrying ({Attempt}/{Total}).",
                    userId, entry.Sequence, attempt, attempts);
            }
        }

        // 重试用尽仍冲突：审计写不进去时**不能**假装成功，否则这条读取就没有痕迹了。
        LogError(
            "Failed to append record access audit for user {UserId} on {ResourceType}/{ResourceId} after {Attempts} attempts.",
            userId, resourceType, resourceId, attempts);
        return Fail("Failed to record data access audit entry.", 500);
    }

    /// <inheritdoc />
    public async Task<Result> VerifyChainAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return Ok();
        }

        // 链要整条读才验得了，所以不按租户过滤行；但别租户用户的链对租户内的调用者等同于不存在
        //（404，不泄露存在性）。链上混有别租户 / 无租户行（切换过租户的用户、多租户开启前的旧行）
        // 时同样拒绝：失败关闭，宿主照常能验。
        var entries = await _repository.AsQueryable()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);

        var scope = TenantScope;
        if (scope.IsPinned && entries.Any(e => !scope.CanAccess(e.TenantId)))
        {
            return Fail($"Audit chain not found for user {userId}.", 404, CoreErrorCodes.RESOURCE_NOT_FOUND);
        }

        var expectedPrevious = string.Empty;
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.PreviousHash, expectedPrevious, StringComparison.Ordinal))
            {
                return Fail($"Audit chain broken for user {userId} at sequence {entry.Sequence}: previous hash mismatch.", 409);
            }

            if (!string.Equals(entry.Hash, ComputeHash(entry), StringComparison.Ordinal))
            {
                return Fail($"Audit chain broken for user {userId} at sequence {entry.Sequence}: entry has been altered.", 409);
            }

            expectedPrevious = entry.Hash;
        }

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<RecordAccessDto>>> GetAccessesAsync(
        RecordAccessQueryDto query,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        if (!_options.CurrentValue.Enabled)
        {
            // 未启用时表都不存在，返回空页而不是让调用方去判断开关。
            return Ok((IPagedList<RecordAccessDto>)new PagedList<RecordAccessDto>(
                [], query.PageIndex, query.PageSize, 0));
        }

        var queryable = ApplyFilters(ScopedAccesses(), query);

        // 按时间倒序：最近一次访问排在最前，这是查「谁刚看过」时想要的顺序。
        var paged = await queryable.OrderByDescending(e => e.CreationTime)
            .ThenByDescending(e => e.Sequence)
            .CreateAsync(query.PageIndex, query.PageSize);

        var items = paged.Items.MapToList<RecordAccessDto>();
        var result = new PagedList<RecordAccessDto>(items, paged.PageIndex, paged.PageSize, paged.TotalCount);

        return Ok((IPagedList<RecordAccessDto>)result);
    }

    /// <inheritdoc />
    public async Task<Result<List<RecordAccessUserStatDto>>> GetUserStatisticsAsync(
        DateTime? startTime = null,
        DateTime? endTime = null,
        int topN = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return Ok(new List<RecordAccessUserStatDto>());
        }

        if (topN <= 0)
        {
            return Fail<List<RecordAccessUserStatDto>>("topN must be greater than zero.", 400);
        }

        var queryable = ScopedAccesses();

        if (startTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime <= endTime.Value);
        }

        var stats = await queryable
            .GroupBy(e => new { e.UserId, e.UserName })
            .Select(g => new RecordAccessUserStatDto
            {
                UserId = g.Key.UserId,
                UserName = g.Key.UserName,
                AccessCount = g.Count(),
                DistinctRecordCount = g.Select(e => e.ResourceType + ":" + e.ResourceId).Distinct().Count(),
                LastAccessTime = g.Max(e => e.CreationTime)
            })
            .OrderByDescending(s => s.AccessCount)
            .Take(topN)
            .ToListAsync(cancellationToken);

        return Ok(stats);
    }

    /// <summary>
    /// 把查询条件应用到查询上。
    /// </summary>
    private static IQueryable<AuditRecordAccess> ApplyFilters(
        IQueryable<AuditRecordAccess> queryable,
        RecordAccessQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            var resourceType = query.ResourceType.ToLower();
            queryable = queryable.Where(e => e.ResourceType.ToLower() == resourceType);
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            queryable = queryable.Where(e => e.ResourceId == query.ResourceId);
        }

        if (query.UserId.HasValue)
        {
            queryable = queryable.Where(e => e.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Purpose))
        {
            var purpose = query.Purpose.ToLower();
            queryable = queryable.Where(e => e.Purpose != null && e.Purpose.ToLower() == purpose);
        }

        if (query.StartTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime <= query.EndTime.Value);
        }

        return queryable;
    }

    /// <summary>
    /// 检查该用户在最近一小时内的读取量是否已达配额。
    /// </summary>
    private async Task<Result> CheckQuotaAsync(
        Guid? userId,
        RecordAccessAuditOptions options,
        CancellationToken cancellationToken)
    {
        if (options.MaxReadsPerUserPerHour <= 0 || userId == null)
        {
            return Ok();
        }

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await _repository.AsQueryable()
            .Where(e => e.UserId == userId && e.CreationTime >= since)
            .CountAsync(cancellationToken);

        if (recent < options.MaxReadsPerUserPerHour)
        {
            return Ok();
        }

        // 这条日志是给安全运营看的：达到配额通常意味着批量导出正在发生。
        LogWarning(
            "User {UserId} reached the record access quota ({Limit} reads/hour); further reads are being refused.",
            userId, options.MaxReadsPerUserPerHour);

        return Fail("Data access quota exceeded. Please contact your administrator.", 429);
    }

    private async Task<AuditRecordAccess?> LoadChainTailAsync(Guid? userId, CancellationToken cancellationToken)
    {
        return await _repository.AsQueryable()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 计算条目哈希：覆盖链上一条的哈希与本条的全部关键字段。
    /// </summary>
    /// <remarks>
    /// 字段以不可能出现在内容里的分隔符连接，避免「把 ResourceType 的尾部挪到 ResourceId 的头部」
    /// 这类拼接歧义产生相同的哈希。时间用往返格式（"O"）与不变文化，
    /// 否则换个服务器区域设置就会算出不同的哈希。
    /// </remarks>
    private static string ComputeHash(AuditRecordAccess entry)
    {
        var payload = string.Join(
            '\u001F',
            entry.PreviousHash,
            entry.Sequence.ToString(CultureInfo.InvariantCulture),
            entry.ResourceType,
            entry.ResourceId,
            entry.Purpose ?? string.Empty,
            entry.UserId?.ToString() ?? string.Empty,
            entry.CreationTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// 判断异常是否为唯一索引冲突（并发抢同一序号）。
    /// </summary>
    /// <remarks>
    /// 不同数据库提供者抛出的类型与错误码各不相同，这里按「更新异常」粗粒度识别：
    /// 本表的唯一约束只有 <c>(UserId, Sequence)</c> 一条，因此更新异常在这条路径上
    /// 基本只可能是它。识别错了的代价只是多重试一次。
    /// </remarks>
    private static bool IsConcurrencyConflict(Exception ex)
        => ex is DbUpdateException or InvalidOperationException;
}
