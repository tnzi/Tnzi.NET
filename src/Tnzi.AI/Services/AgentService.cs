
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 管理服务实现
/// </summary>
public class AgentService : ApplicationService, IAgentService
{
    /// <summary>
    /// Maximum records loaded for in-memory JSON field filtering (Domain/Role)
    /// </summary>
    private const int MaxInMemoryFilterRecords = 5000;

    private readonly IRepository<Agent, Guid> _repository;
    private readonly IRepository<AgentVersion, Guid> _versionRepository;

    /// <summary>
    /// 执行入口是路由门面而不是 <see cref="IAgentRuntime"/>：绑定了外部 CLI 运行时的 Agent
    /// 由门面改道到外部执行域，本服务对此无感（签名逐字相同）。
    /// </summary>
    private readonly IAgentDispatchFacade _runtime;
    private readonly IAgentGrantService _grantService;

    public AgentService(
        IRepository<Agent, Guid> repository,
        IRepository<AgentVersion, Guid> versionRepository,
        IAgentDispatchFacade runtime,
        IAgentGrantService grantService,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _versionRepository = Check.NotNull(versionRepository);
        _runtime = Check.NotNull(runtime);
        _grantService = Check.NotNull(grantService);
    }

    public async Task<Result<AgentDto>> CreateAsync(CreateAgentDto input)
    {
        Check.NotNull(input);
        var entity = input.MapTo<Agent>();
        entity.Configuration = AgentExecutionConfigDto.Serialize(input.ExecutionConfig);

        // Atomic write: the entity insert + the three grant reconciles must commit (or roll back) as a unit.
        // Global UoW is OFF by default, so without this an exception after the entity insert leaves the
        // entity persisted without its grants. The insert's SaveChanges inside the txn assigns entity.Id
        // before the reconciles read it. (Mapster does not map ToolGroups/SkillSlugs/KnowledgeBaseIds onto
        // the entity - those columns no longer exist; grants are the sole source of truth.)
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _repository.InsertAsync(entity, ct);

            // Junction grants are authoritative.
            // populated list = create grants, null = no grants, [] = clear (no-op on fresh row).
            await _grantService.ReconcileToolGroupsAsync(entity.Id, input.ToolGroups, ct);
            await _grantService.ReconcileSkillsAsync(entity.Id, input.SkillSlugs, ct);
            await _grantService.ReconcileKnowledgeAsync(entity.Id, input.KnowledgeBaseIds, ct);
        });

        return Ok(await MapToDtoAsync(entity));
    }

    public async Task<Result<AgentDto>> UpdateAsync(Guid id, UpdateAgentDto input)
    {
        Check.NotNull(input);
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        // Create version snapshot before applying changes
        await CreateVersionSnapshotAsync(entity, input.ChangeNote);

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Instructions != null) entity.Instructions = input.Instructions;
        if (input.Provider != null) entity.Provider = input.Provider;
        if (input.Model != null) entity.Model = input.Model;
        if (input.Temperature.HasValue) entity.Temperature = input.Temperature;
        if (input.MaxTokens.HasValue) entity.MaxTokens = input.MaxTokens;
        if (input.TimeoutSeconds.HasValue) entity.TimeoutSeconds = input.TimeoutSeconds;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;
        if (input.Domains != null) entity.Domains = input.Domains;
        if (input.Roles != null) entity.Roles = input.Roles;
        if (input.QualityTier.HasValue) entity.QualityTier = input.QualityTier.Value;
        if (input.LatencyTier.HasValue) entity.LatencyTier = input.LatencyTier.Value;
        if (input.CostTier.HasValue) entity.CostTier = input.CostTier.Value;
        // Resource assignments (ToolGroups/SkillSlugs/KnowledgeBaseIds) are written ONLY to the grant
        // junctions below - the entity no longer carries those columns.
        // Persona - tri-state: null = no change, empty string = clear, non-empty = set.
        if (input.Persona != null)
        {
            entity.Persona = input.Persona.Length == 0 ? null : input.Persona;
        }
        var executionModeChanged = input.ExecutionMode.HasValue && input.ExecutionMode.Value != entity.ExecutionMode;
        if (input.ExecutionMode.HasValue) entity.ExecutionMode = input.ExecutionMode.Value;
        if (input.ExecutionConfig != null || executionModeChanged) entity.Configuration = AgentExecutionConfigDto.Serialize(input.ExecutionConfig);

        // Atomic write: entity update + grant reconciles commit/roll back as a unit (see CreateAsync).
        // Snapshot was already captured ABOVE (outside the txn) so it reflects pre-mutation state regardless.
        // tri-state: null = no change, [] = clear, populated = replace.
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _repository.UpdateAsync(entity, ct);
            await _grantService.ReconcileToolGroupsAsync(entity.Id, input.ToolGroups, ct);
            await _grantService.ReconcileSkillsAsync(entity.Id, input.SkillSlugs, ct);
            await _grantService.ReconcileKnowledgeAsync(entity.Id, input.KnowledgeBaseIds, ct);
        });

        return Ok(await MapToDtoAsync(entity));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail("Agent not found", 404, ErrorCodes.AgentNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<AgentDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        return Ok(await MapToDtoAsync(entity));
    }

    public async Task<Result<AgentDto>> CloneAsync(Guid id, string? newName = null)
    {
        var source = await _repository.GetAsync(id);
        if (source == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var clone = new Agent
        {
            Name = newName ?? $"{source.Name} (Copy)",
            Description = source.Description,
            Instructions = source.Instructions,
            Provider = source.Provider,
            Model = source.Model,
            Temperature = source.Temperature,
            MaxTokens = source.MaxTokens,
            TimeoutSeconds = source.TimeoutSeconds,
            IsEnabled = source.IsEnabled,
            ExecutionMode = source.ExecutionMode,
            Configuration = source.Configuration,
            Domains = source.Domains?.ToList(),
            Roles = source.Roles?.ToList(),
            QualityTier = source.QualityTier,
            LatencyTier = source.LatencyTier,
            CostTier = source.CostTier,
            // Carry the persona (soul) content to the clone - it is a meaningful
            // part of the agent's identity, not transient state.
            Persona = source.Persona,
            // Resource assignments (tool groups / skills / knowledge) are carried via the grant
            // reconcile from sourceGrants below - the entity no longer holds those columns.
        };

        // Read the source's authoritative grants (a read of a DIFFERENT agent - safe outside the txn).
        var sourceGrants = await _grantService.GetGrantsAsync(source.Id);

        // Atomic write: the clone insert + the three grant reconciles commit/roll back as a unit (see CreateAsync).
        // The insert's SaveChanges inside the txn assigns clone.Id before the reconciles read it.
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _repository.InsertAsync(clone, ct);

            // Carry the source agent's grants to the clone (junction is authoritative).
            await _grantService.ReconcileToolGroupsAsync(clone.Id, sourceGrants.ToolGroups, ct);
            await _grantService.ReconcileSkillsAsync(clone.Id, sourceGrants.SkillSlugs, ct);
            await _grantService.ReconcileKnowledgeAsync(clone.Id, sourceGrants.KnowledgeBaseIds, ct);
        });

        Logger.LogInformation("Agent cloned: {SourceId} -> {CloneId}, Name: {Name}", id, clone.Id, clone.Name);
        return Ok(await MapToDtoAsync(clone));
    }

    public async Task<Result<IPagedList<AgentVersionDto>>> GetVersionsAsync(Guid agentId, AgentVersionQueryDto query)
    {
        var agentExists = await _repository.AnyAsync(a => a.Id == agentId);
        if (!agentExists)
            return Fail<IPagedList<AgentVersionDto>>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var pagedList = await _versionRepository
            .Where(v => v.AgentId == agentId)
            .OrderByDescending(v => v.Version)
            .ProjectTo<AgentVersion, AgentVersionDto>()
            .CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result<AgentVersionDto>> GetVersionAsync(Guid agentId, int version)
    {
        var entity = await _versionRepository
            .Where(v => v.AgentId == agentId && v.Version == version)
            .FirstOrDefaultAsync();

        if (entity == null)
            return Fail<AgentVersionDto>("Agent version not found", 404, ErrorCodes.AgentVersionNotFound);

        return Ok(entity.MapTo<AgentVersionDto>());
    }

    public async Task<Result<AgentDto>> RollbackToVersionAsync(Guid agentId, int version)
    {
        var agent = await _repository.GetAsync(agentId);
        if (agent == null)
            return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var versionEntity = await _versionRepository
            .Where(v => v.AgentId == agentId && v.Version == version)
            .FirstOrDefaultAsync();

        if (versionEntity == null)
            return Fail<AgentDto>("Agent version not found", 404, ErrorCodes.AgentVersionNotFound);

        // Create snapshot of current state before rollback
        await CreateVersionSnapshotAsync(agent, $"Before rollback to version {version}");

        // Restore agent from config snapshot
        var snapshot = JsonSerializer.Deserialize<AgentConfigSnapshot>(versionEntity.ConfigSnapshot);
        if (snapshot == null)
            return Fail<AgentDto>("Failed to deserialize version snapshot", 500, ErrorCodes.InternalError);

        agent.Name = snapshot.Name;
        agent.Description = snapshot.Description;
        agent.Instructions = snapshot.Instructions;
        agent.Provider = snapshot.Provider;
        agent.Model = snapshot.Model;
        agent.Temperature = snapshot.Temperature;
        agent.MaxTokens = snapshot.MaxTokens;
        agent.TimeoutSeconds = snapshot.TimeoutSeconds;
        agent.IsEnabled = snapshot.IsEnabled;
        agent.ExecutionMode = snapshot.ExecutionMode;
        agent.Configuration = snapshot.Configuration;
        agent.Domains = snapshot.Domains;
        agent.Roles = snapshot.Roles;
        agent.QualityTier = snapshot.QualityTier;
        agent.LatencyTier = snapshot.LatencyTier;
        agent.CostTier = snapshot.CostTier;
        agent.Persona = snapshot.Persona;
        // ToolGroups/SkillSlugs/KnowledgeBaseIds are restored ONLY into the grant junctions below
        // (the snapshot still carries them) - the entity no longer holds those columns.

        // Atomic write: entity update + grant reconciles commit/roll back as a unit (see CreateAsync).
        // Snapshot of the pre-rollback state was already captured ABOVE (outside the txn).
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _repository.UpdateAsync(agent, ct);

            // Restore the grants from the snapshot lists so the junction matches the rolled-back resources.
            // Tool groups (GrantType=Group) and per-tool grants (GrantType=Tool) are reconciled
            // independently so neither clobbers the other.
            await _grantService.ReconcileToolGroupsAsync(agent.Id, snapshot.ToolGroups, ct);
            await _grantService.ReconcileToolNamesAsync(agent.Id, snapshot.ToolNames, ct);
            await _grantService.ReconcileSkillsAsync(agent.Id, snapshot.SkillSlugs, ct);
            await _grantService.ReconcileKnowledgeAsync(agent.Id, snapshot.KnowledgeBaseIds, ct);
        });

        Logger.LogInformation("Agent {AgentId} rolled back to version {Version}", agentId, version);
        return Ok(await MapToDtoAsync(agent));
    }

    public async Task<Result<IPagedList<AgentDto>>> GetListAsync(AgentListQueryDto query)
    {
        var queryable = _repository
            .WhereIf(a => a.Name.ToLower().Contains(query.Keyword!.ToLower()) || (a.Description != null && a.Description.ToLower().Contains(query.Keyword!.ToLower())),
                !string.IsNullOrWhiteSpace(query.Keyword))
            .WhereIf(a => a.Provider == query.Provider, !string.IsNullOrWhiteSpace(query.Provider))
            .WhereIf(a => a.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
            .WhereIf(a => a.ExecutionMode == query.ExecutionMode!.Value, query.ExecutionMode.HasValue)
            .WhereIf(a => a.QualityTier >= query.MinQualityTier!.Value, query.MinQualityTier.HasValue)
            .WhereIf(a => a.LatencyTier <= query.MaxLatencyTier!.Value, query.MaxLatencyTier.HasValue)
            .WhereIf(a => a.CostTier <= query.MaxCostTier!.Value, query.MaxCostTier.HasValue)
            .OrderByDescending(a => a.CreationTime);

        var hasJsonFilter = !string.IsNullOrWhiteSpace(query.Domain) || !string.IsNullOrWhiteSpace(query.Role);

        if (!hasJsonFilter)
        {
            var pagedList = await queryable.ProjectTo<Agent, AgentDto>().CreateAsync(query);
            // ProjectTo maps the JSON columns into the DTO; override with the authoritative grants.
            await ApplyGrantsToPageAsync(pagedList.Items);
            return Ok(pagedList);
        }

        // JSON 字段无法在数据库级过滤，加载后在内存中过滤并正确计算分页
        // 限制最大加载量防止大表全量加载导致 OOM
        var entities = await queryable.Take(MaxInMemoryFilterRecords).ToListAsync();
        var filtered = entities
            .Where(a => string.IsNullOrWhiteSpace(query.Domain) || (a.Domains != null && a.Domains.Exists(d => string.Equals(d, query.Domain, StringComparison.OrdinalIgnoreCase))))
            .Where(a => string.IsNullOrWhiteSpace(query.Role) || (a.Roles != null && a.Roles.Exists(r => string.Equals(r, query.Role, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var totalCount = filtered.Count;
        var pagedEntities = filtered
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();
        // 与上面的非 JSON 过滤分支一致：先整页映射，再用批量授权投影覆盖资源列表，
        // 避免逐条 MapToDtoAsync 触发「每 Agent 三次 grant 查询」的 N+1。
        var pagedItems = new List<AgentDto>(pagedEntities.Count);
        foreach (var entity in pagedEntities)
        {
            var dto = entity.MapTo<AgentDto>();
            dto.ExecutionConfig = AgentExecutionConfigDto.Deserialize(entity.Configuration);
            pagedItems.Add(dto);
        }
        await ApplyGrantsToPageAsync(pagedItems);

        return Ok<IPagedList<AgentDto>>(new PagedList<AgentDto>(pagedItems, query.PageIndex, query.PageSize, totalCount));
    }

    /// <summary>
    /// Set each DTO's resource lists from the authoritative grant projection.
    /// 批量获取本页全部 Agent 的授权（三表各一次 IN 查询），消除原先的逐条 N+1。
    /// </summary>
    private async Task ApplyGrantsToPageAsync(IList<AgentDto> items)
    {
        if (items.Count == 0) return;

        var grantsByAgent = await _grantService.GetGrantsAsync(items.Select(d => d.Id).ToList());
        foreach (var dto in items)
        {
            if (!grantsByAgent.TryGetValue(dto.Id, out var grants)) continue;
            ApplyGrantsToDto(dto, grants);
        }
    }

    /// <summary>把一份授权投影写入 DTO 的资源列表（空投影折叠为 null，保持 null-when-empty 语义）。</summary>
    private static void ApplyGrantsToDto(AgentDto dto, AgentGrantsProjection grants)
    {
        dto.ToolGroups = grants.ToolGroups.Count > 0 ? grants.ToolGroups.ToList() : null;
        dto.SkillSlugs = grants.SkillSlugs.Count > 0 ? grants.SkillSlugs.ToList() : null;
        dto.KnowledgeBaseIds = grants.KnowledgeBaseIds.Count > 0 ? grants.KnowledgeBaseIds.ToList() : null;
    }

    public async Task<Result<AgentResponseDto>> RunAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, CancellationToken ct = default)
    {
        try
        {
            var runRequest = new AgentRunRequest
            {
                OperationType = AIOperationType.AgentRun,
                AgentId = agentId,
                UserMessage = message,
                ContentParts = content,
                ThreadId = threadId,
                UserId = userId
            };

            var result = await _runtime.RunAsync(runRequest, ct);
            if (TryMapFailure(result, out var statusCode, out var errorCode))
            {
                return Fail<AgentResponseDto>(result.Response, statusCode, errorCode);
            }

            return Ok(new AgentResponseDto
            {
                Content = result.Response,
                FinishReason = result.FinishReason,
                Model = result.Model,
                Provider = result.Provider,
                Status = result.Status,
                Usage = result.Usage,
                Citations = result.Citations,
                HandoffPath = result.HandoffPath,
                FinalAgentName = result.FinalAgentName,
                Reasoning = result.Reasoning,
                Suggestions = result.Suggestions,
                Artifacts = result.Artifacts,
                ClarificationQuestion = result.ClarificationQuestion
            });
        }
        catch (BusinessException ex)
        {
            return Fail<AgentResponseDto>(ex.Message, ex.HttpStatusCode, ex.Code);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Agent run failed: AgentId={AgentId}, ThreadId={ThreadId}, Message={Message}",
                agentId, threadId, ex.Message);
            return Fail<AgentResponseDto>("Agent execution failed.", 500, ErrorCodes.AgentRunFailed);
        }
    }

    private static bool TryMapFailure(AgentRunResult result, out int statusCode, out string errorCode)
        => AgentStreamMapper.TryMapFailure(result, ErrorCodes.AgentRunFailed, out statusCode, out errorCode);

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runRequest = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            AgentId = agentId,
            UserMessage = message,
            ContentParts = content,
            ThreadId = threadId,
            UserId = userId
        };

        int inputTokens = 0, outputTokens = 0;
        AgentStreamChunk? lastChunk = null;
        string? streamErrorMessage = null;
        string? streamFinishReason = null;
        string? streamModel = null;
        List<CitationDto>? citations = null;

        await foreach (var chunk in _runtime.RunStreamingAsync(runRequest, ct).WithCancellation(ct))
        {
            lastChunk = chunk;

            // HistoryMiddleware 在首批 chunk 到达前已完成线程自动创建，直接从 runRequest 读取。
            var currentThreadId = runRequest.ThreadId;

            // 提取 Token 使用信息
            var (inp, outp) = ChatMessageHelper.ExtractStreamingUsage(chunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }

            // 收集 Citations（通常只在最终 chunk 中包含）
            if (chunk.Citations != null)
            {
                citations = chunk.Citations;
            }
            if (chunk.FinishReason != null)
            {
                streamFinishReason = chunk.FinishReason;
            }
            if (!string.IsNullOrWhiteSpace(chunk.Model))
            {
                streamModel = chunk.Model;
            }

            // 发送 delta 事件、工具调用状态事件或错误事件（guardrail 拦截等）
            if (chunk.Error != null)
            {
                streamErrorMessage = chunk.Error;
            }

            var streamEvent = CreateStreamEvent(chunk, currentThreadId, ErrorCodes.AgentRunFailed);
            if (streamEvent != null)
            {
                yield return streamEvent;
            }
        }

        // 如果流式响应结束时还没有 Token 信息，尝试从最后一条 chunk 中查找
        if (lastChunk is not null && inputTokens == 0 && outputTokens == 0)
        {
            var (inp, outp) = ChatMessageHelper.ExtractStreamingUsage(lastChunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }
        }

        // 发送终止事件
        var hasStreamError = streamErrorMessage != null;
        yield return new StreamEvent
        {
            IsDone = true,
            FinishReason = hasStreamError ? (streamFinishReason ?? "error") : (streamFinishReason ?? "stop"),
            Model = streamModel,
            ThreadId = runRequest.ThreadId,
            IsError = hasStreamError,
            ErrorMessage = hasStreamError ? streamErrorMessage : null,
            Usage = new TokenUsageDto
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens
            },
            Citations = citations
        };
    }

    private static StreamEvent? CreateStreamEvent(AgentStreamChunk chunk, Guid? threadId, string defaultErrorCode)
        => AgentStreamMapper.BuildStreamEvent(chunk, threadId, defaultErrorCode);

    public async Task<Result<AgentDto>> ConfigureAbTestAsync(Guid agentId, ConfigureAbTestDto input)
    {
        Check.NotNull(input);

        var agent = await _repository.GetAsync(agentId);
        if (agent == null)
            return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        if (input.VersionA == input.VersionB)
            return Fail<AgentDto>("Version A and B must be different", 400, ErrorCodes.InvalidContent);

        // 验证两个版本都存在
        var versionAExists = await _versionRepository.AnyAsync(v => v.AgentId == agentId && v.Version == input.VersionA);
        if (!versionAExists)
            return Fail<AgentDto>($"Version {input.VersionA} not found", 404, ErrorCodes.AgentVersionNotFound);

        var versionBExists = await _versionRepository.AnyAsync(v => v.AgentId == agentId && v.Version == input.VersionB);
        if (!versionBExists)
            return Fail<AgentDto>($"Version {input.VersionB} not found", 404, ErrorCodes.AgentVersionNotFound);

        // 将 A/B 测试配置合并到 Agent.Configuration JSON
        var abTestConfig = new AbTestConfig
        {
            Enabled = true,
            VersionA = input.VersionA,
            VersionB = input.VersionB,
            TrafficPercentB = input.TrafficPercentB
        };
        agent.Configuration = AgentVersionRouter.MergeAbTestConfig(agent.Configuration, abTestConfig);
        await _repository.UpdateAsync(agent);

        Logger.LogInformation(
            "A/B test configured for Agent {AgentId}: VersionA={VersionA}, VersionB={VersionB}, TrafficPercentB={TrafficPercentB}",
            agentId, input.VersionA, input.VersionB, input.TrafficPercentB);

        return Ok(await MapToDtoAsync(agent));
    }

    public async Task<Result<AgentDto>> StopAbTestAsync(Guid agentId)
    {
        var agent = await _repository.GetAsync(agentId);
        if (agent == null)
            return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        // 移除 A/B 测试配置
        agent.Configuration = AgentVersionRouter.MergeAbTestConfig(agent.Configuration, null);

        // 如果 Configuration 变为空对象 "{}" 且没有其他配置，设为 null
        if (agent.Configuration == "{}")
            agent.Configuration = null;

        await _repository.UpdateAsync(agent);

        Logger.LogInformation("A/B test stopped for Agent {AgentId}", agentId);
        return Ok(await MapToDtoAsync(agent));
    }

    private async Task CreateVersionSnapshotAsync(Agent entity, string? changeNote)
    {
        try
        {
            var latestVersion = await _versionRepository
                .Where(v => v.AgentId == entity.Id)
                .OrderByDescending(v => v.Version)
                .Select(v => (int?)v.Version)
                .FirstOrDefaultAsync();

            var nextVersion = (latestVersion ?? 0) + 1;

            var snapshot = entity.MapTo<AgentConfigSnapshot>();

            // Resources (ToolGroups/SkillSlugs/KnowledgeBaseIds) no longer live on the entity - they are
            // owned by the grant junctions. Capture them from the authoritative grant projection so version
            // history (and the rollback that restores grants from these lists) stays intact.
            var grants = await _grantService.GetGrantsAsync(entity.Id);
            snapshot.ToolGroups = grants.ToolGroups.Count > 0 ? grants.ToolGroups.ToList() : null;
            snapshot.ToolNames = grants.ToolNames.Count > 0 ? grants.ToolNames.ToList() : null;
            snapshot.SkillSlugs = grants.SkillSlugs.Count > 0 ? grants.SkillSlugs.ToList() : null;
            snapshot.KnowledgeBaseIds = grants.KnowledgeBaseIds.Count > 0 ? grants.KnowledgeBaseIds.ToList() : null;

            var versionEntity = new AgentVersion
            {
                AgentId = entity.Id,
                Version = nextVersion,
                ChangeNote = changeNote,
                ConfigSnapshot = JsonSerializer.Serialize(snapshot)
            };

            await _versionRepository.InsertAsync(versionEntity);
            Logger.LogDebug("Created version snapshot {Version} for Agent {AgentId}", nextVersion, entity.Id);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create version snapshot for Agent {AgentId}", entity.Id);
        }
    }

    /// <summary>
    /// Maps an Agent entity to its DTO. The resource lists (ToolGroups/SkillSlugs/KnowledgeBaseIds)
    /// are projected SOLELY from the junction grants - the entity no longer carries those columns,
    /// so the wire-shape lists on the DTO are filled exclusively from the grant projection.
    /// </summary>
    private async Task<AgentDto> MapToDtoAsync(Agent entity)
    {
        var dto = entity.MapTo<AgentDto>();
        dto.ExecutionConfig = AgentExecutionConfigDto.Deserialize(entity.Configuration);
        await OverrideDtoGrantsAsync(dto);
        return dto;
    }

    /// <summary>
    /// Set a DTO's resource lists from the authoritative grant projection (in place).
    /// Grants are the sole source of truth: an empty projection collapses to null (preserving the
    /// downstream null-when-empty semantics - no per-agent whitelist vs. a whitelist of nothing).
    /// </summary>
    private async Task OverrideDtoGrantsAsync(AgentDto dto)
    {
        var grants = await _grantService.GetGrantsAsync(dto.Id);
        ApplyGrantsToDto(dto, grants);
    }
}

/// <summary>
/// Agent config snapshot serialization model (internal)
/// </summary>
internal class AgentConfigSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<string>? ToolGroups { get; set; }
    /// <summary>Per-tool grants (GrantType=Tool) at snapshot time so rollback / A/B routing restore single-tool authorizations.</summary>
    public List<string>? ToolNames { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool IsEnabled { get; set; }
    public AgentExecutionMode ExecutionMode { get; set; }
    public string? Configuration { get; set; }
    public List<string>? Domains { get; set; }
    public List<string>? Roles { get; set; }
    public int QualityTier { get; set; }
    public int LatencyTier { get; set; }
    public int CostTier { get; set; }
    /// <summary>Persona (soul) content at snapshot time so rollback restores it.</summary>
    public string? Persona { get; set; }
    /// <summary>Knowledge base assignments at snapshot time.</summary>
    public List<Guid>? KnowledgeBaseIds { get; set; }
    /// <summary>Skill assignments at snapshot time.</summary>
    public List<string>? SkillSlugs { get; set; }
}
