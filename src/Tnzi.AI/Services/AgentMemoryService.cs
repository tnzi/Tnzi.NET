
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 记忆管理服务实现 - 直接操作 <see cref="MemoryEntry"/> 仓储，按结构化 <c>AgentId</c> 列过滤。
/// </summary>
public class AgentMemoryService : ApplicationService, IAgentMemoryService
{
    private readonly IRepository<MemoryEntry, Guid> _repository;

    public AgentMemoryService(IRepository<MemoryEntry, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    /// <summary>agent-bound scope key（与运行时 MemoryContributor 的 ForAgent(agentId) 对齐）。</summary>
    private static string ScopeFor(Guid agentId) => MemoryScope.ForAgent(agentId).ToScopeKey();

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

            var queryable = _repository.AsQueryable()
                .Where(e => e.AgentId == agentId)
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
        var entry = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(e => e.Id == memoryId, ct);
        if (entry == null || entry.AgentId != agentId)
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
        var entry = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(e => e.Id == memoryId, ct);
        if (entry == null || entry.AgentId != agentId)
            return Fail("Memory entry not found", 404, ErrorCodes.MemoryNotFound);

        await _repository.DeleteAsync(entry);
        return Ok();
    }
}
