namespace Tnzi.AI.Services;

/// <summary>
/// Agent 人格服务实现
/// </summary>
public class AgentPersonaService : ApplicationService, IAgentPersonaService
{
    private readonly IRepository<AgentPersona, Guid> _repository;

    public AgentPersonaService(IServiceProvider serviceProvider, IRepository<AgentPersona, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<AgentPersonaDto>> CreateAsync(CreateAgentPersonaDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        // 检查 Slug 唯一性
        var existing = await _repository.FirstOrDefaultAsync(
            e => e.Slug == input.Slug, ct);
        if (existing != null)
            return Fail<AgentPersonaDto>($"Persona with slug '{input.Slug}' already exists.", 409);

        var entity = input.MapTo<AgentPersona>();
        await _repository.InsertAsync(entity, ct);
        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<AgentPersonaDto>> UpdateAsync(Guid id, UpdateAgentPersonaDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        if (input.Slug != null && input.Slug != entity.Slug)
        {
            var existing = await _repository.FirstOrDefaultAsync(
                e => e.Slug == input.Slug && e.Id != id, ct);
            if (existing != null)
                return Fail<AgentPersonaDto>($"Persona with slug '{input.Slug}' already exists.", 409);
        }

        input.MapTo(entity);
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
        var entity = await _repository.GetAsync(id, ct);
        if (entity == null)
            return Fail("Persona not found.", 404);

        if (entity.IsSystem)
            return Fail("Cannot delete system persona.", 403);

        await _repository.DeleteAsync(id, ct);

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
        var entity = await _repository.GetAsync(id, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<AgentPersonaDto>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(slug);

        var entity = await _repository.FirstOrDefaultAsync(
            e => e.Slug == slug, ct);
        if (entity == null)
            return Fail<AgentPersonaDto>("Persona not found.", 404);

        return Ok(entity.MapTo<AgentPersonaDto>());
    }

    public async Task<Result<IPagedList<AgentPersonaDto>>> GetListAsync(AgentPersonaQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        var queryable = _repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(e =>
                e.Name.ToLower().Contains(keyword) ||
                e.Slug.ToLower().Contains(keyword) ||
                (e.Description != null && e.Description.ToLower().Contains(keyword)));
        }

        if (query.IsSystem.HasValue)
            queryable = queryable.Where(e => e.IsSystem == query.IsSystem.Value);

        var pagedList = await queryable
            .OrderByDescending(e => e.CreationTime)
            .ProjectTo<AgentPersona, AgentPersonaDto>()
            .CreateAsync(query, ct);

        return Ok(pagedList);
    }
}
