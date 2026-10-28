
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 记忆管理服务实现 - 直接操作 <see cref="MemoryEntry"/> 仓储，只管该 Agent 的 agent-bound 记忆。
/// </summary>
/// <remarks>
/// 只按 <c>AgentId</c> 过滤是不够的：开启 Agent 隔离后，用户与该 Agent 对话沉淀的个人记忆
/// （<c>save_memory</c> / AutoPersist 写下的 <c>user:{userId}:agent:{agentId}:...</c> 行）同样带着 <c>AgentId</c>。
/// 管理端（<c>ai.agent.view</c> / <c>ai.agent.update</c>）因此能看见、改写、删除每个用户的私有记忆，
/// 而 DTO 不带归属人，连是谁的都分不出来。查询、更新、删除一律只认 agent-bound 行：
/// 不带 <c>UserId</c>、scope 键是该 Agent 的 <c>agent-bound:{agentId}:</c> 前缀（与 <see cref="MemoryScope.ForAgent"/> 同一形状，
/// 不限定记忆类别名，运行时用哪个类别名写的都算）。
/// </remarks>
public class AgentMemoryService : ApplicationService, IAgentMemoryService
{
    private readonly IRepository<MemoryEntry, Guid> _repository;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    public AgentMemoryService(IRepository<MemoryEntry, Guid> repository, IServiceProvider serviceProvider, IOptionsMonitor<AIOptions>? aiOptions = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _aiOptions = aiOptions;
    }

    /// <summary>
    /// 新建记忆写入的 agent-bound scope 键：类别名取运行时同一个配置（<c>AI:ContextProviders:Memory:DefaultScope</c>），
    /// 与 <c>MemoryContributor</c> 加载用的 <c>MemoryScope.ForAgent(agentId, DefaultScope)</c> 逐字一致。
    /// 写死 <c>default</c> 时，改过这个配置的部署在这里新建的记忆一条都不会被注入。
    /// </summary>
    private string ScopeFor(Guid agentId)
        => MemoryScope.ForAgent(agentId, _aiOptions?.CurrentValue.ContextProviders.Memory.DefaultScope ?? "default").ToScopeKey();

    /// <summary>该 Agent 任一类别名的 agent-bound scope 键都以它开头。</summary>
    private static string AgentBoundScopePrefix(Guid agentId) => $"agent-bound:{agentId:N}:";

    /// <summary>
    /// 管理端可见的行：属于该 Agent、不归任何用户、scope 是该 Agent 的 agent-bound 键。
    /// </summary>
    private IQueryable<MemoryEntry> AgentBoundEntries(Guid agentId, bool withTracking = false)
    {
        var prefix = AgentBoundScopePrefix(agentId);
        return _repository.AsQueryable(withTracking)
            .Where(e => e.AgentId == agentId && e.UserId == null && e.Scope.StartsWith(prefix));
    }

    private static AgentMemoryDto ToDto(MemoryEntry e) => new()
    {
        Id = e.Id,
        Content = e.Content,
        Category = e.Category,
        Importance = e.Importance,
        Source = e.Source,
        AccessCount = e.AccessCount,
        LastAccessedTime = e.LastAccessedTime,
        CreationTime = e.CreationTime
    };

    /// <inheritdoc />
    public async Task<Result<IPagedList<AgentMemoryDto>>> GetListAsync(Guid agentId, AgentMemoryListQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);
        try
        {
            var keyword = query.Keyword?.Trim().ToLower();
            var category = query.Category?.Trim();

            var queryable = AgentBoundEntries(agentId)
                .WhereIf(e => e.Category == category, !string.IsNullOrEmpty(category))
                .WhereIf(e => e.Content.ToLower().Contains(keyword!), !string.IsNullOrEmpty(keyword))
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.CreationTime);

            var totalCount = await queryable.CountAsync(ct);
            var items = await queryable
                .Skip((query.PageIndex - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            var paged = new PagedList<AgentMemoryDto>(items.Select(ToDto).ToList(), query.PageIndex, query.PageSize, totalCount);
            return Ok<IPagedList<AgentMemoryDto>>(paged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing memory for agent {AgentId}", agentId);
            return Fail<IPagedList<AgentMemoryDto>>("Failed to list agent memory", 500, ErrorCodes.InternalError);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AgentMemoryDto>> CreateAsync(Guid agentId, CreateAgentMemoryDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Content))
            return Fail<AgentMemoryDto>("Memory content is required", 400, ErrorCodes.InvalidContent);

        var content = dto.Content.Trim();
        var scope = ScopeFor(agentId);
        var contentHash = MemoryContentHasher.Compute(content);

        // 精确重复硬防线：同 scope 下相同内容只允许一行（与 (Scope, ContentHash) 唯一索引一致）。
        var duplicateExists = await _repository.AsQueryable()
            .AnyAsync(e => e.Scope == scope && e.ContentHash == contentHash, ct);
        if (duplicateExists)
            return Fail<AgentMemoryDto>("An identical memory entry already exists for this agent", 409, ErrorCodes.InvalidContent);

        var entry = new MemoryEntry
        {
            AgentId = agentId,
            Scope = scope,
            Content = content,
            ContentHash = contentHash,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            Importance = dto.Importance,
            Source = "admin"
        };
        await _repository.InsertAsync(entry);
        return Ok(ToDto(entry));
    }

    /// <inheritdoc />
    public async Task<Result<AgentMemoryDto>> UpdateAsync(Guid agentId, Guid memoryId, UpdateAgentMemoryDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);
        var entry = await AgentBoundEntries(agentId, withTracking: true).FirstOrDefaultAsync(e => e.Id == memoryId, ct);
        if (entry == null)
            return Fail<AgentMemoryDto>("Memory entry not found", 404, ErrorCodes.MemoryNotFound);

        if (dto.Content != null)
        {
            var content = dto.Content.Trim();
            var contentHash = MemoryContentHasher.Compute(content);

            // 改内容后撞上同 scope 的另一行 → 409（与唯一索引一致，避免 DbUpdateException 变 500）。
            var duplicateExists = await _repository.AsQueryable()
                .AnyAsync(e => e.Scope == entry.Scope && e.ContentHash == contentHash && e.Id != entry.Id, ct);
            if (duplicateExists)
                return Fail<AgentMemoryDto>("An identical memory entry already exists for this agent", 409, ErrorCodes.InvalidContent);

            entry.Content = content;
            entry.ContentHash = contentHash;
        }
        if (dto.Category != null) entry.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        if (dto.Importance.HasValue) entry.Importance = dto.Importance.Value;

        await _repository.UpdateAsync(entry);
        return Ok(ToDto(entry));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid agentId, Guid memoryId, CancellationToken ct = default)
    {
        var entry = await AgentBoundEntries(agentId, withTracking: true).FirstOrDefaultAsync(e => e.Id == memoryId, ct);
        if (entry == null)
            return Fail("Memory entry not found", 404, ErrorCodes.MemoryNotFound);

        await _repository.DeleteAsync(entry);
        return Ok();
    }
}
