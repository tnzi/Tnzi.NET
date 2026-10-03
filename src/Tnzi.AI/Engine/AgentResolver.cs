namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 解析器实现 - Agent 解析和消息构建逻辑
/// </summary>
public class AgentResolver : IAgentResolver
{
    private readonly IAgentFactory _agentFactory;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IUserToolPermissionResolver _permissionResolver;
    private readonly IPromptTemplateEngine _templateEngine;
    private readonly IAgentVersionRouter _versionRouter;
    private readonly IAgentGrantService _grantService;
    private readonly IWorkspaceAgentProvider? _workspaceAgentProvider;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICurrentUser? _currentUser;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;
    private readonly ILogger<AgentResolver> _logger;

    public AgentResolver(
        IAgentFactory agentFactory,
        IOptionsMonitor<AIOptions> options,
        IRepository<Agent, Guid> agentRepository,
        IUserToolPermissionResolver permissionResolver,
        IPromptTemplateEngine templateEngine,
        IAgentVersionRouter versionRouter,
        IAgentGrantService grantService,
        ILogger<AgentResolver> logger,
        IWorkspaceAgentProvider? workspaceAgentProvider = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _agentFactory = Check.NotNull(agentFactory);
        _options = Check.NotNull(options);
        _agentRepository = Check.NotNull(agentRepository);
        _permissionResolver = Check.NotNull(permissionResolver);
        _templateEngine = Check.NotNull(templateEngine);
        _versionRouter = Check.NotNull(versionRouter);
        _grantService = Check.NotNull(grantService);
        _logger = Check.NotNull(logger);
        _workspaceAgentProvider = workspaceAgentProvider;
        _executionContextAccessor = executionContextAccessor;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _multiTenancyOptions = multiTenancyOptions;
    }

    /// <inheritdoc />
    public async Task<AgentResolution> ResolveAgentAsync(Guid? agentId, string? provider, string? model, List<string>? toolGroups, CancellationToken ct, List<string>? toolNames = null)
    {
        var defaultProvider = provider ?? _options.CurrentValue.DefaultProvider;

        // 1. 优先使用 AgentId（加载已定义的 Agent）
        if (agentId.HasValue)
        {
            // 租户调用者还要解析得到宿主级共享定义（YAML Agent，TenantId 为 null）；单租户 / 宿主原样。
            var scope = SharedAgentScope.Resolve(_multiTenancyOptions, _currentTenant, _currentUser);
            var entity = scope.IsTenantCaller
                ? await scope.Apply(_agentRepository.AsQueryable()).FirstOrDefaultAsync(a => a.Id == agentId.Value, ct)
                : await _agentRepository.GetAsync(agentId.Value, ct);
            if (entity == null)
            {
                // Try workspace fallback before returning failure
                if (_workspaceAgentProvider != null && _options.CurrentValue.Workspace.Enabled)
                {
                    var wsAgent = await _workspaceAgentProvider.LoadAsync(
                        _options.CurrentValue.Workspace.GlobalPath, agentId.Value.ToString(), ct);
                    if (wsAgent != null)
                    {
                        var wsProvider = wsAgent.Provider ?? defaultProvider;
                        var wsModel = model ?? wsAgent.Model;
                        var wsInstructions = wsAgent.Instructions ?? string.Empty;
                        // Honor workspace AGENT.md frontmatter `executionMode: Handoff|AgentAsTools|Router|Single`.
                        // Unknown / missing values fall back to Single (default for DB agents).
                        var wsExecutionMode = ParseExecutionMode(wsAgent.ExecutionMode);
                        // wsAgent.Temperature is float? but the factory takes double? - widen safely.
                        var wsTemperature = wsAgent.Temperature.HasValue ? (double?)wsAgent.Temperature.Value : null;
                        // 工作区 agent 与 DB agent 过同一道 RequiredPermissions 门：frontmatter 里的工具组
                        // 是文件作者写的，不是调用者的授权；传 null 会让 sandbox / task / a2a 对任何调用者放行。
                        var wsUserPermissions = await _permissionResolver.ResolveAsync(wsAgent.ToolGroups, null, ct);
                        var wsExecutor = await _agentFactory.CreateAgentAsync(
                            wsProvider, wsModel, wsInstructions, wsAgent.Name,
                            wsAgent.ToolGroups, wsTemperature, wsAgent.MaxTokens,
                            options: null, userPermissions: wsUserPermissions, ct: ct);
                        // Provide CreationParameters so SkillConstraintMiddleware can rebuild
                        // the executor when a skill triggers a model/provider override.
                        var wsCreationParams = new AgentCreationParameters(
                            wsInstructions, wsAgent.Name, wsAgent.ToolGroups,
                            wsTemperature, wsAgent.MaxTokens, wsUserPermissions);
                        return AgentResolution.Success(
                            wsExecutor, wsProvider, wsModel, agentId,
                            agentConfiguration: null,
                            executionMode: wsExecutionMode,
                            creationParameters: wsCreationParams,
                            personaContent: string.IsNullOrWhiteSpace(wsAgent.PersonaContent) ? null : wsAgent.PersonaContent);
                    }
                }

                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentNotFound);
            }
            if (!entity.IsEnabled)
            {
                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentDisabled);
            }

            // 钉住版本（评估）：加载该版本快照，不再 A/B 分流；否则走 A/B 测试路由（可能替换为不同版本的配置）。
            // 版本号随 AgentRunRequest 走，经执行上下文读取（ResolveAgentAsync 的签名不再加参数）。
            var pinnedVersion = _executionContextAccessor?.CurrentRequest?.AgentVersionNumber;
            var routeResult = pinnedVersion.HasValue
                ? await _versionRouter.RouteToVersionAsync(entity, pinnedVersion.Value, ct)
                : await _versionRouter.RouteAsync(entity, ct);
            entity = routeResult.Agent;

            // 资源授权（junction grant）是工具组/单工具/技能/知识库的唯一权威来源（JSON 列已删除）。
            // A/B 路由时，变体的资源来自版本快照（routeResult.SnapshotGrants）而非 live junction——
            // 否则路由到变体 B 却静默使用 live 资源，A/B 实验对资源配置失效。passthrough 时 SnapshotGrants
            // 为 null，回退读取 live grants（保持既有行为）。
            var grants = routeResult.SnapshotGrants ?? await _grantService.GetGrantsAsync(entity.Id, ct);
            var entityToolGroups = grants.ToolGroups.Count > 0 ? grants.ToolGroups.ToList() : null;
            // per-tool 授权（GrantType=Tool）：展开为单工具，与工具组并行流入 factory。
            var entityToolNames = grants.ToolNames.Count > 0 ? grants.ToolNames.ToList() : null;
            // null-when-empty (load-bearing): SkillSlugs/KnowledgeBaseIds MUST be null - not [] - when there are
            // no grants. An empty list ≠ null downstream: a populated skill list = whitelist; null =
            // "no per-agent whitelist → fall back to SkillDefinition.Agents name-wildcard filtering"; [] = "whitelist
            // of nothing". So collapse empty → null here.
            var knowledgeBaseIds = NullIfEmpty(grants.KnowledgeBaseIds);
            var skillSlugs = NullIfEmpty(grants.SkillSlugs);

            var userPermissions = await _permissionResolver.ResolveAsync(entityToolGroups, entityToolNames, ct);

            // 渲染 Agent Instructions 模板变量（{{date}}, {{user.name}} 等）
            var renderedInstructions = _templateEngine.Render(
                entity.Instructions ?? string.Empty,
                new Dictionary<string, string> { ["agent.name"] = entity.Name });

            // model param acts as an override (e.g. think-model auto-switch); fall back to entity default
            var effectiveModel = model ?? entity.Model;
            var executor = await _agentFactory.CreateAgentAsync(
                entity.Provider,
                effectiveModel,
                renderedInstructions,
                entity.Name,
                entityToolGroups,
                entity.Temperature,
                entity.MaxTokens,
                options: null,
                userPermissions: userPermissions,
                toolNames: entityToolNames,
                agentId: entity.Id,
                ct: ct);
            var creationParams = new AgentCreationParameters(renderedInstructions, entity.Name, entityToolGroups, entity.Temperature, entity.MaxTokens, userPermissions, entityToolNames);
            return AgentResolution.Success(executor, entity.Provider, effectiveModel, agentId, entity.Configuration, entity.ExecutionMode, creationParams, personaContent: string.IsNullOrWhiteSpace(entity.Persona) ? null : entity.Persona, knowledgeBaseIds: knowledgeBaseIds, skillSlugs: skillSlugs);
        }

        // 2. 使用 ToolGroups / ToolNames（无 AgentId 但有工具组或 per-request 单工具覆盖）
        var hasToolGroups = toolGroups is { Count: > 0 };
        var hasToolNames = toolNames is { Count: > 0 };
        if (hasToolGroups || hasToolNames)
        {
            var adHocGroups = hasToolGroups ? toolGroups : null;
            var adHocNames = hasToolNames ? toolNames : null;
            var userPermissions = await _permissionResolver.ResolveAsync(adHocGroups, adHocNames, ct);
            var executor = await _agentFactory.CreateAgentAsync(defaultProvider, model, null, null, adHocGroups, options: null, userPermissions: userPermissions, toolNames: adHocNames, ct: ct);
            return AgentResolution.Success(executor, defaultProvider, model, null);
        }

        // 3. 仅 Provider/Model（无 AgentId 也无 ToolGroups/ToolNames）
        var defaultExecutor = await _agentFactory.CreateAgentAsync(defaultProvider, model, options: null, ct: ct);
        return AgentResolution.Success(defaultExecutor, defaultProvider, model, null);
    }

    /// <inheritdoc />
    public Task<ChatMessage> BuildChatMessageAsync(string? message, List<ContentPartDto>? content, CancellationToken ct)
    {
        // 1. 纯文本模式（向后兼容）
        if (content == null || content.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new BusinessException("Either Message or Content must be provided", ErrorCodes.InvalidContent, 400);
            }
            return Task.FromResult(new ChatMessage(ChatRole.User, message!));
        }

        // 2. 多模态模式：构建包含正确 AIContent 类型的 ChatMessage
        var contents = new List<AIContent>();

        foreach (var part in content)
        {
            switch (part)
            {
                case TextContentPartDto textPart:
                    if (!string.IsNullOrWhiteSpace(textPart.Text))
                    {
                        contents.Add(new TextContent(textPart.Text));
                    }
                    break;

                case ImageContentPartDto imagePart:
                    contents.Add(BuildImageContent(imagePart));
                    break;

                case FileContentPartDto filePart:
                    if (filePart.FileId == Guid.Empty)
                    {
                        throw new BusinessException("File content must have a valid FileId", ErrorCodes.InvalidContent, 400);
                    }
                    // 文件引用暂以文本占位符表示（需要 Storage 模块集成后实现完整的文件内容加载）
                    var fileName = filePart.FileName ?? filePart.FileId.ToString();
                    contents.Add(new TextContent($"[File: {fileName} (ID: {filePart.FileId})]"));
                    break;

                default:
                    _logger.LogWarning("Unknown content part type: {Type}", part.GetType().Name);
                    break;
            }
        }

        if (contents.Count == 0)
        {
            throw new BusinessException("Content must contain at least one non-empty part", ErrorCodes.InvalidContent, 400);
        }

        return Task.FromResult(new ChatMessage(ChatRole.User, contents));
    }

    /// <summary>
    /// Collapse an empty (or null) resource list to <c>null</c> so the load-bearing
    /// null-vs-empty downstream semantics are preserved (see resolver DB-agent branch).
    /// </summary>
    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? list)
        => list is { Count: > 0 } ? list : null;

    /// <summary>
    /// Parse a workspace `executionMode` frontmatter value (case-insensitive) into the
    /// AgentExecutionMode enum. Unknown / empty values fall back to Single.
    /// </summary>
    private static AgentExecutionMode ParseExecutionMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return AgentExecutionMode.Single;
        return Enum.TryParse<AgentExecutionMode>(raw.Trim(), ignoreCase: true, out var mode)
            ? mode
            : AgentExecutionMode.Single;
    }

    /// <summary>
    /// 从 ImageContentPartDto 构建 MEAI DataContent
    /// </summary>
    private static DataContent BuildImageContent(ImageContentPartDto imagePart)
    {
        if (!string.IsNullOrWhiteSpace(imagePart.Url))
        {
            if (!Uri.TryCreate(imagePart.Url, UriKind.Absolute, out var imageUri) ||
                (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps && !imageUri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BusinessException("Invalid image URL: must be HTTP(S) or data URI", ErrorCodes.InvalidContent, 400);
            }
            return new DataContent(imageUri, imagePart.MediaType ?? "image/png");
        }

        if (!string.IsNullOrWhiteSpace(imagePart.Base64Data))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(imagePart.Base64Data);
            }
            catch (FormatException)
            {
                throw new BusinessException("Invalid Base64 data in image content", ErrorCodes.InvalidContent, 400);
            }
            return new DataContent(bytes, imagePart.MediaType ?? "image/png");
        }

        throw new BusinessException("Image content must have either Url or Base64Data", ErrorCodes.InvalidContent, 400);
    }
}
