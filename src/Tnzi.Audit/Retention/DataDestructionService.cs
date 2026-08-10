using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;

namespace Tnzi.Audit.Retention;

/// <summary>
/// <see cref="IDataDestructionService"/> 的默认实现。
/// </summary>
/// <remarks>
/// <para>
/// 每条策略走同一条流水线：<strong>查到期 → 问保全 → 销毁 → 出证明</strong>。
/// 顺序不能变：先出证明再销毁，会在销毁失败时留下一份说谎的证明；
/// 先销毁再问保全，问出来也晚了。
/// </para>
/// <para>
/// <strong>没有到期数据时不出证明。</strong>证明链是销毁发生过的证据，不是心跳信号；
/// 每天塞一条「今天销毁了 0 条」只会把真正的销毁记录淹掉。
/// </para>
/// </remarks>
public class DataDestructionService : ApplicationService, IDataDestructionService
{
    /// <summary>
    /// 哈希载荷的字段分隔符（单元分隔符）。
    /// </summary>
    /// <remarks>
    /// 选一个不可能出现在内容里的字符，避免「把一个字段的尾部挪到下一个字段的头部」
    /// 这类拼接歧义产生相同的哈希。
    /// </remarks>
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// 证明写入撞上链尾争用时的重试次数。
    /// </summary>
    /// <remarks>
    /// 用尽后数据已销毁而证明未落库，这是文档里写明的已知窄窗口
    /// （<c>docs/modules/audit.md</c> 的「证明写入失败时会发生什么」）——
    /// 改这个数字要连文档一起改。
    /// </remarks>
    private const int CertificateWriteAttempts = 3;

    private static readonly MethodInfo ExecutePolicyMethod =
        typeof(DataDestructionService).GetMethod(
            nameof(ExecutePolicyAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo CollectTenantIdsMethod =
        typeof(DataDestructionService).GetMethod(
            nameof(CollectTenantIdsAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly IRepository<AuditDataDestruction, Guid> _repository;
    private readonly IOptionsMonitor<DataDestructionOptions> _options;
    private readonly IEnumerable<IRetentionPolicyProvider> _policyProviders;
    private readonly IEnumerable<ILitigationHoldProvider> _holdProviders;
    private readonly IDataDestroyer _destroyer;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<FieldEncryptionOptions>? _encryptionOptions;
    private readonly ICurrentTenant _currentTenant;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化 <see cref="DataDestructionService"/>。
    /// </summary>
    /// <param name="serviceProvider">服务提供程序（按实体类型解析仓储）。</param>
    /// <param name="repository">销毁证明仓储。</param>
    /// <param name="options">数据销毁选项。</param>
    /// <param name="policyProviders">保留策略提供者（可为空集合）。</param>
    /// <param name="holdProviders">诉讼保全提供者（可为空集合，表示没有保全）。</param>
    /// <param name="destroyer">销毁动作。</param>
    /// <param name="currentTenant">当前租户上下文，用于按租户隔离地销毁。</param>
    /// <param name="encryptionOptions">
    /// 字段加密选项，用于回查策略声明的密钥是否已被销毁。
    /// 可选：没启用字段加密的应用照样可以做保留期销毁。
    /// </param>
    /// <param name="multiTenancyOptions">多租户开关；未启用时整库视为单一逻辑租户。</param>
    public DataDestructionService(
        IServiceProvider serviceProvider,
        IRepository<AuditDataDestruction, Guid> repository,
        IOptionsMonitor<DataDestructionOptions> options,
        IEnumerable<IRetentionPolicyProvider> policyProviders,
        IEnumerable<ILitigationHoldProvider> holdProviders,
        IDataDestroyer destroyer,
        ICurrentTenant currentTenant,
        IOptionsMonitor<FieldEncryptionOptions>? encryptionOptions = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
        _policyProviders = Check.NotNull(policyProviders);
        _holdProviders = Check.NotNull(holdProviders);
        _destroyer = Check.NotNull(destroyer);
        _currentTenant = Check.NotNull(currentTenant);
        _encryptionOptions = encryptionOptions;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <inheritdoc />
    public async Task<Result<DataDestructionRunDto>> RunAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var run = new DataDestructionRunDto { IsDryRun = options.DryRun };

        if (!options.Enabled)
        {
            return Ok(run);
        }

        var policies = _policyProviders.SelectMany(p => p.GetPolicies()).ToList();
        if (policies.Count == 0)
        {
            // 开了开关却没有策略，多半是接线漏了——而这类漏接的表现是「安静地什么都不销毁」。
            LogWarning(
                "Data destruction is enabled but no retention policy is declared. "
                + "Register an IRetentionPolicyProvider, otherwise nothing will ever be destroyed.");
            return Ok(run);
        }

        var duplicates = policies.GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            // 同名策略会让证明链上的记录无法归属到确定的一条策略。
            return Fail<DataDestructionRunDto>(
                $"Duplicate retention policy names: {string.Join(", ", duplicates)}. Policy names must be unique.",
                409);
        }

        foreach (var policy in policies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Policies.Add(await RunPolicySafelyAsync(policy, options, cancellationToken));
        }

        return Ok(run);
    }

    /// <inheritdoc />
    public async Task<Result> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return Ok();
        }

        var entries = await _repository.AsQueryable()
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);

        var expectedPrevious = string.Empty;
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.PreviousHash, expectedPrevious, StringComparison.Ordinal))
            {
                return Fail($"Destruction certificate chain broken at sequence {entry.Sequence}: previous hash mismatch.", 409);
            }

            if (!string.Equals(entry.Hash, ComputeHash(entry), StringComparison.Ordinal))
            {
                return Fail($"Destruction certificate chain broken at sequence {entry.Sequence}: entry has been altered.", 409);
            }

            expectedPrevious = entry.Hash;
        }

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<DataDestructionDto>>> GetCertificatesAsync(
        DataDestructionQueryDto query,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.PolicyName))
        {
            var name = query.PolicyName.ToLower();
            queryable = queryable.Where(e => e.PolicyName.ToLower() == name);
        }

        if (query.StartTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            queryable = queryable.Where(e => e.CreationTime <= query.EndTime.Value);
        }

        if (query.IsDryRun.HasValue)
        {
            queryable = queryable.Where(e => e.IsDryRun == query.IsDryRun.Value);
        }

        // 按链序号倒序：最近一次销毁排在最前，也正好是链尾。
        var paged = await queryable.OrderByDescending(e => e.Sequence)
            .CreateAsync(query.PageIndex, query.PageSize);

        var items = paged.Items.MapToList<DataDestructionDto>();
        var result = new PagedList<DataDestructionDto>(items, paged.PageIndex, paged.PageSize, paged.TotalCount);

        return Ok((IPagedList<DataDestructionDto>)result);
    }

    /// <summary>
    /// 跑一条策略，把异常收敛成该策略的失败结果。
    /// </summary>
    /// <remarks>
    /// 单条策略失败不中断其余策略：一条策略的实体类型配错了，
    /// 不该让其它策略的到期数据一直堆着。
    /// </remarks>
    private async Task<DataDestructionPolicyResultDto> RunPolicySafelyAsync(
        RetentionPolicy policy,
        DataDestructionOptions options,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - policy.RetentionPeriod;

        try
        {
            return await ForEachTenantAsync(policy, options, cutoff, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 反射调用把真实异常裹在 TargetInvocationException 里。
            var actual = (ex as TargetInvocationException)?.InnerException ?? ex;

            // 直调 Logger：ApplicationService.LogError 没有带异常的重载，而堆栈是这里最有价值的信息。
            Logger.LogError(actual,
                "Retention policy '{PolicyName}' failed for entity {EntityType}.",
                policy.Name, policy.EntityType.FullName);

            return new DataDestructionPolicyResultDto
            {
                PolicyName = policy.Name,
                EntityType = policy.EntityType.FullName ?? policy.EntityType.Name,
                Cutoff = cutoff,
                Error = actual.Message
            };
        }
    }

    /// <summary>
    /// 按租户隔离地跑一条策略。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>★这一层是必需的，不是防御性编程。</strong>到期扫描用
    /// <c>IgnoreQueryFilters()</c> 才能看见已软删除的行，而 EF 的全局过滤器是
    /// <strong>一个</strong>——软删除与多租户合成同一个表达式，关掉一个就等于两个都关。
    /// 后台服务又跑在没有租户上下文的作用域里，于是不隔离的话，
    /// <strong>一条策略会扫到并销毁所有租户的到期数据</strong>，而销毁不可逆。
    /// </para>
    /// <para>
    /// 做法与 <c>Tnzi.Storage</c> 的文件清理一致：先跨租户取出有到期数据的租户清单，
    /// 再逐个 <c>ICurrentTenant.Change</c> 切进去执行——切进去之后，
    /// 该租户的过滤器在 <c>IgnoreQueryFilters</c> 之外仍由显式条件兜住。
    /// </para>
    /// <para>
    /// 未启用多租户时整库是单一逻辑租户，直接跑一次，零额外查询。
    /// </para>
    /// </remarks>
    private async Task<DataDestructionPolicyResultDto> ForEachTenantAsync(
        RetentionPolicy policy,
        DataDestructionOptions options,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        if (!_multiTenancyEnabled || !typeof(IMultiTenant).IsAssignableFrom(policy.EntityType))
        {
            return await InvokePolicyAsync(policy, options, cutoff, cancellationToken);
        }

        var tenantIds = await (Task<List<Guid?>>)CollectTenantIdsMethod
            .MakeGenericMethod(policy.EntityType)
            .Invoke(this, [policy, cutoff, cancellationToken])!;

        var merged = new DataDestructionPolicyResultDto
        {
            PolicyName = policy.Name,
            EntityType = policy.EntityType.FullName ?? policy.EntityType.Name,
            Cutoff = cutoff
        };

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (_currentTenant.Change(tenantId))
            {
                var perTenant = await InvokePolicyAsync(policy, options, cutoff, cancellationToken);

                merged.DestroyedCount += perTenant.DestroyedCount;
                merged.HeldCount += perTenant.HeldCount;
                merged.HasMore |= perTenant.HasMore;
                merged.CertificateId ??= perTenant.CertificateId;

                // 一个租户失败不该让其余租户的到期数据一直堆着，但失败必须可见。
                if (perTenant.Error != null)
                {
                    merged.Error = merged.Error == null
                        ? perTenant.Error
                        : $"{merged.Error}; {perTenant.Error}";
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// 反射调用泛型的策略执行方法。
    /// </summary>
    private Task<DataDestructionPolicyResultDto> InvokePolicyAsync(
        RetentionPolicy policy,
        DataDestructionOptions options,
        DateTime cutoff,
        CancellationToken cancellationToken)
        => (Task<DataDestructionPolicyResultDto>)ExecutePolicyMethod
            .MakeGenericMethod(policy.EntityType)
            .Invoke(this, [policy, options, cutoff, cancellationToken])!;

    /// <summary>
    /// 跨租户取出「有到期数据」的租户清单。
    /// </summary>
    /// <remarks>
    /// 只取有到期数据的租户，而不是遍历租户表：没有到期数据的租户不需要被切进去，
    /// 也就不会为它写出一条「销毁 0 条」的证明。
    /// </remarks>
    private async Task<List<Guid?>> CollectTenantIdsAsync<TEntity>(
        RetentionPolicy policy,
        DateTime cutoff,
        CancellationToken cancellationToken)
        where TEntity : class, IEntity
    {
        var typed = (RetentionPolicy<TEntity>)policy;
        var repository = _serviceProvider.GetRequiredService<IRepository<TEntity>>();

        var query = repository.AsQueryable().IgnoreQueryFilters();
        if (typed.Scope != null)
        {
            query = query.Where(typed.Scope);
        }

        return await query
            .Where(BuildExpiredPredicate(typed.Timestamp, cutoff))
            .Select(e => ((IMultiTenant)e).TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 对单条策略执行「查到期 → 问保全 → 销毁 → 出证明」。
    /// </summary>
    private async Task<DataDestructionPolicyResultDto> ExecutePolicyAsync<TEntity>(
        RetentionPolicy policy,
        DataDestructionOptions options,
        DateTime cutoff,
        CancellationToken cancellationToken)
        where TEntity : class, IEntity
    {
        var typed = (RetentionPolicy<TEntity>)policy;
        var result = new DataDestructionPolicyResultDto
        {
            PolicyName = policy.Name,
            EntityType = typeof(TEntity).FullName ?? typeof(TEntity).Name,
            Cutoff = cutoff
        };

        var repository = _serviceProvider.GetRequiredService<IRepository<TEntity>>();

        // IgnoreQueryFilters：已软删除的行同样占着库、同样在备份里，保留期对它们一视同仁。
        var query = repository.AsQueryable().IgnoreQueryFilters();
        if (typed.Scope != null)
        {
            query = query.Where(typed.Scope);
        }

        var candidates = await query
            .Where(BuildExpiredPredicate(typed.Timestamp, cutoff))
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            // 无到期数据不出证明：证明链是销毁的证据，不是心跳。
            return result;
        }

        result.HasMore = candidates.Count == options.BatchSize;

        var identifiers = candidates.ToDictionary(FormatIdentifier, e => e, StringComparer.Ordinal);
        var held = await CollectHeldIdentifiersAsync(policy, typeof(TEntity), identifiers.Keys, cancellationToken);

        var survivors = identifiers
            .Where(pair => !held.Contains(pair.Key))
            .ToList();

        result.HeldCount = candidates.Count - survivors.Count;

        if (survivors.Count == 0)
        {
            // 整批都被保全：这值得留一份证明，否则「到期了却一条没销毁」看起来像漏跑。
            result.CertificateId = await WriteCertificateAsync(
                policy, cutoff, destroyedCount: 0, result.HeldCount, [], options, cancellationToken);
            return result;
        }

        var survivingEntities = survivors.Select(pair => pair.Value).ToList();
        var survivingIds = survivors.Select(pair => pair.Key).ToList();

        result.DestroyedCount = options.DryRun
            ? survivingEntities.Count
            : await _destroyer.DestroyAsync(survivingEntities, cancellationToken);

        result.CertificateId = await WriteCertificateAsync(
            policy, cutoff, result.DestroyedCount, result.HeldCount, survivingIds, options, cancellationToken);

        LogInformation(
            "Retention policy '{PolicyName}' destroyed {Destroyed} record(s) of {EntityType} older than {Cutoff:O} "
            + "({Held} held){DryRun}.",
            policy.Name, result.DestroyedCount, result.EntityType, cutoff, result.HeldCount,
            options.DryRun ? " [dry run]" : string.Empty);

        return result;
    }

    /// <summary>
    /// 汇总所有保全提供者认为该暂缓销毁的标识（并集）。
    /// </summary>
    /// <remarks>
    /// 任一提供者抛异常都会向上传播中止本策略：保全系统查不通时宁可不销毁——
    /// 晚一天销毁只是延迟，销毁了不该销毁的无法撤销。
    /// </remarks>
    private async Task<HashSet<string>> CollectHeldIdentifiersAsync(
        RetentionPolicy policy,
        Type entityType,
        ICollection<string> candidates,
        CancellationToken cancellationToken)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        var candidateList = candidates.ToList();

        foreach (var provider in _holdProviders)
        {
            var ids = await provider.GetHeldIdentifiersAsync(policy.Name, entityType, candidateList, cancellationToken);
            if (ids != null)
            {
                held.UnionWith(ids);
            }
        }

        return held;
    }

    /// <summary>
    /// 追加一条销毁证明到全局哈希链。
    /// </summary>
    private async Task<Guid?> WriteCertificateAsync(
        RetentionPolicy policy,
        DateTime cutoff,
        int destroyedCount,
        int heldCount,
        IReadOnlyList<string> destroyedIdentifiers,
        DataDestructionOptions options,
        CancellationToken cancellationToken)
    {
        const int attempts = CertificateWriteAttempts;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var tail = await _repository.AsQueryable()
                .OrderByDescending(e => e.Sequence)
                .FirstOrDefaultAsync(cancellationToken);

            var entry = new AuditDataDestruction
            {
                Sequence = (tail?.Sequence ?? 0) + 1,
                PolicyName = policy.Name,
                EntityType = policy.EntityType.FullName ?? policy.EntityType.Name,
                Cutoff = cutoff,
                DestroyedCount = destroyedCount,
                HeldCount = heldCount,
                IdentifierDigest = ComputeIdentifierDigest(destroyedIdentifiers),
                Identifiers = options.StoreIdentifiers && destroyedIdentifiers.Count > 0
                    ? JsonSerializer.Serialize(destroyedIdentifiers)
                    : null,
                Mode = options.DryRun ? $"{_destroyer.Mode} (dry-run)" : _destroyer.Mode,
                EncryptionKeyId = policy.EncryptionKeyId,
                IsKeyDestroyed = IsEncryptionKeyDestroyed(policy.EncryptionKeyId),
                IsDryRun = options.DryRun,
                ExecutedByUserId = CurrentUser?.Id,
                // 同 AuditRecordAccess：本表不实现 IMultiTenant，租户归属手工带上。
                // 定时触发时当前用户为空，此处自然为 null——见类注释里的多租户说明。
                TenantId = CurrentUser?.TenantId,
                PreviousHash = tail?.Hash ?? string.Empty,
                CreationTime = DateTime.UtcNow
            };
            entry.Hash = ComputeHash(entry);

            try
            {
                await _repository.InsertAsync(entry);
                return entry.Id;
            }
            catch (Exception ex) when (attempt < attempts && ex is DbUpdateException or InvalidOperationException)
            {
                LogInformation(
                    "Destruction certificate chain conflict at sequence {Sequence}, retrying ({Attempt}/{Total}).",
                    entry.Sequence, attempt, attempts);
            }
        }

        // 数据已经销毁但证明写不进去：这是必须被看见的状态，不能安静地返回成功。
        LogError(
            "Destroyed {Count} record(s) under policy '{PolicyName}' but failed to write the destruction certificate "
            + "after {Attempts} attempts. The destruction is NOT provable.",
            destroyedCount, policy.Name, attempts);

        throw new InvalidOperationException(
            $"Failed to write the destruction certificate for policy '{policy.Name}' after {attempts} attempts.");
    }

    /// <summary>
    /// 回查策略声明的加密密钥是否已不在密钥环里。
    /// </summary>
    /// <remarks>
    /// <strong>字段加密未启用时恒为 <c>false</c>。</strong>此时密钥环本来就是空的，
    /// 若据此判定「密钥已销毁」，等于给每一份证明都盖上一个它没有资格盖的章。
    /// </remarks>
    private bool IsEncryptionKeyDestroyed(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return false;
        }

        var encryption = _encryptionOptions?.CurrentValue;
        if (encryption is not { Enabled: true })
        {
            return false;
        }

        return !encryption.Keys.ContainsKey(keyId);
    }

    /// <summary>
    /// 构造 <c>e =&gt; timestamp(e) &lt; cutoff</c>。
    /// </summary>
    private static Expression<Func<TEntity, bool>> BuildExpiredPredicate<TEntity>(
        Expression<Func<TEntity, DateTime>> timestamp,
        DateTime cutoff)
    {
        var body = Expression.LessThan(timestamp.Body, Expression.Constant(cutoff));
        return Expression.Lambda<Func<TEntity, bool>>(body, timestamp.Parameters);
    }

    /// <summary>
    /// 把实体主键格式化成字符串标识（复合键以逗号连接）。
    /// </summary>
    private static string FormatIdentifier(IEntity entity)
        => string.Join(',', entity.GetKeys().Select(k => k?.ToString() ?? string.Empty));

    /// <summary>
    /// 计算被销毁标识的集合摘要。
    /// </summary>
    /// <remarks>
    /// 先排序再连接，因此与销毁顺序无关：同一批记录无论以什么顺序被删，摘要都一样，
    /// 持有原始清单的人才能复算验证。
    /// </remarks>
    private static string ComputeIdentifierDigest(IReadOnlyList<string> identifiers)
    {
        if (identifiers.Count == 0)
        {
            return Convert.ToHexStringLower(SHA256.HashData([]));
        }

        var ordered = identifiers.OrderBy(id => id, StringComparer.Ordinal);
        var payload = string.Join(FieldSeparator, ordered);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// 计算条目哈希：覆盖链上一条的哈希与本条的全部关键字段。
    /// </summary>
    /// <remarks>
    /// 字段以不可能出现在内容里的分隔符连接，避免拼接歧义。时间用往返格式（"O"）
    /// 与不变文化，否则换个服务器区域设置就会算出不同的哈希。
    /// </remarks>
    private static string ComputeHash(AuditDataDestruction entry)
    {
        var payload = string.Join(
            FieldSeparator,
            entry.PreviousHash,
            entry.Sequence.ToString(CultureInfo.InvariantCulture),
            entry.PolicyName,
            entry.EntityType,
            entry.Cutoff.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.DestroyedCount.ToString(CultureInfo.InvariantCulture),
            entry.HeldCount.ToString(CultureInfo.InvariantCulture),
            entry.IdentifierDigest,
            entry.Mode,
            entry.EncryptionKeyId ?? string.Empty,
            entry.IsKeyDestroyed ? "1" : "0",
            entry.IsDryRun ? "1" : "0",
            entry.CreationTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
