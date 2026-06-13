namespace Tnzi.AI.Services;

/// <summary>
/// Agent 人格服务实现。
/// <para>
/// 可见性策略（ResourceScope）：AgentPersona 有意不实现 IMultiTenant — 全局查询过滤器
/// 的严格等值条件会把 System 人格（TenantId=null）对所有真实租户隐藏。取而代之，
/// 每个 AgentPersona 携带 Scope + TenantId，服务层联合过滤：
///   - 多租户模式（currentTenantId != null）：返回 Scope=System ∪ (Scope=Tenant &amp;&amp; TenantId==当前租户)
///   - 单租户模式（currentTenantId == null）：返回全部行（向后兼容）
/// 写入路径保护：租户用户无法修改/删除其他租户的行（→ 404），也无法修改/删除系统行（→ 403）。
/// </para>
/// </summary>
public class AgentPersonaService : ApplicationService, IAgentPersonaService
{
    private readonly IRepository<AgentPersona, Guid> _repository;
    private readonly ICurrentTenant? _currentTenant;

    public AgentPersonaService(
        IServiceProvider serviceProvider,
        IRepository<AgentPersona, Guid> repository,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 对查询应用租户可见性过滤。
    /// 多租户模式下保留 System ∪ 当前租户行；单租户（currentTenant=null）下不添加过滤器。
    /// </summary>
    private IQueryable<AgentPersona> ApplyVisibility(IQueryable<AgentPersona> query)
    {
        var tenantId = _currentTenant?.Id;
        if (tenantId == null)
            return query; // 单租户：无需额外过滤

        return query.Where(e =>
            e.Scope == ResourceScope.System ||
            (e.Scope == ResourceScope.Tenant && e.TenantId == tenantId));
    }

    public async Task<Result<AgentPersonaDto>> CreateAsync(CreateAgentPersonaDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        // 计算作用域：优先使用请求显式值，其次按当前租户上下文推断
        var tenantId = _currentTenant?.Id;
        var scope = input.Scope ?? (tenantId.HasValue ? ResourceScope.Tenant : ResourceScope.System);
        var effectiveTenantId = scope == ResourceScope.Tenant ? tenantId : null;

        // 检查 Slug 唯一性（同一 Scope+TenantId 下）
        var slugExists = await _repository.AsQueryable()
            .AnyAsync(e => e.Slug == input.Slug && e.Scope == scope && e.TenantId == effectiveTenantId, ct);
        if (slugExists)
            return Fail<AgentPersonaDto>($"Persona with slug '{input.Slug}' already exists.", 409);

        var entity = new AgentPersona
        {
            Name = input.Name,
            Slug = input.Slug,
            Content = input.Content,
            Description = input.Description,
            Scope = scope,
            TenantId = effectiveTenantId
        };

        await _repository.InsertAsync(entity, ct);
        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<AgentPersonaDto>> UpdateAsync(Guid id, UpdateAgentPersonaDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        // 通过 ApplyVisibility 加载 — 跨租户行对当前租户不可见 → 404
        var entity = await ApplyVisibility(_repository.AsQueryable(withTracking: true))
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        // 租户用户不能修改系统人格
        if (_currentTenant?.Id != null && entity.Scope == ResourceScope.System)
            return Fail<AgentPersonaDto>("Access denied: system personas are managed by administrators.", 403);

        if (input.Slug != null && input.Slug != entity.Slug)
        {
            var slugExists = await _repository.AsQueryable()
                .AnyAsync(e => e.Slug == input.Slug && e.Scope == entity.Scope && e.TenantId == entity.TenantId && e.Id != id, ct);
            if (slugExists)
                return Fail<AgentPersonaDto>($"Persona with slug '{input.Slug}' already exists.", 409);
            entity.Slug = input.Slug;
        }

        if (input.Name != null) entity.Name = input.Name;
        if (input.Content != null) entity.Content = input.Content;
        if (input.Description != null) entity.Description = input.Description;

        await _repository.UpdateAsync(entity, ct);

        // Notify ContextInjectionMiddleware so cached Soul content for this persona
        // is evicted immediately rather than at the next 5-minute TTL boundary.
        await PublishCacheInvalidationAsync(new AgentPersonaUpdatedEvent
        {
            PersonaId = entity.Id,
            Slug = entity.Slug
        });

        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // 通过 ApplyVisibility 加载 — 跨租户行对当前租户不可见 → 404
        var entity = await ApplyVisibility(_repository.AsQueryable(withTracking: true))
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity == null)
            return Fail("Persona not found.", 404);

        // 租户用户不能删除系统人格
        if (_currentTenant?.Id != null && entity.Scope == ResourceScope.System)
            return Fail("Access denied: system personas are managed by administrators.", 403);

        await _repository.DeleteAsync(entity, ct);

        await PublishCacheInvalidationAsync(new AgentPersonaDeletedEvent
        {
            PersonaId = id
        });

        return Ok();
    }

    /// <summary>
    /// Best-effort fire-and-forget event publish — failure is logged but never
    /// propagated to the caller. Cache invalidation is a hint, not a guarantee.
    /// </summary>
    private async Task PublishCacheInvalidationAsync(EventBase evt)
    {
        try
        {
            var bus = EventBus;
            if (bus == null) return;
            await bus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish persona cache invalidation event ({EventType})", evt.GetType().Name);
        }
    }

    public async Task<Result<AgentPersonaDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await ApplyVisibility(_repository.AsQueryable())
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<AgentPersonaDto>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(slug);

        var entity = await ApplyVisibility(_repository.AsQueryable())
            .FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<IPagedList<AgentPersonaDto>>> GetListAsync(AgentPersonaQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        var queryable = ApplyVisibility(_repository.AsQueryable());

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(e =>
                e.Name.ToLower().Contains(keyword) ||
                e.Slug.ToLower().Contains(keyword) ||
                (e.Description != null && e.Description.ToLower().Contains(keyword)));
        }

        if (query.Scope.HasValue)
            queryable = queryable.Where(e => e.Scope == query.Scope.Value);

        var pagedList = await queryable
            .OrderByDescending(e => e.CreationTime)
            .ProjectTo<AgentPersona, AgentPersonaDto>()
            .CreateAsync(query, ct);

        return Ok(pagedList);
    }
}
