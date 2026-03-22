namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 解析器实现 — Agent 解析和消息构建逻辑
/// </summary>
public class AgentResolver : IAgentResolver
{
    private readonly IAgentFactory _agentFactory;
    private readonly IOptions<AIOptions> _options;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IToolRegistry _toolRegistry;
    private readonly IPromptTemplateEngine _templateEngine;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly ILogger<AgentResolver> _logger;

    public AgentResolver(
        IAgentFactory agentFactory,
        IOptions<AIOptions> options,
        IRepository<Agent, Guid> agentRepository,
        IToolRegistry toolRegistry,
        IPromptTemplateEngine templateEngine,
        ILogger<AgentResolver> logger,
        IPermissionChecker? permissionChecker = null)
    {
        _agentFactory = Check.NotNull(agentFactory);
        _options = Check.NotNull(options);
        _agentRepository = Check.NotNull(agentRepository);
        _toolRegistry = Check.NotNull(toolRegistry);
        _templateEngine = Check.NotNull(templateEngine);
        _logger = Check.NotNull(logger);
        _permissionChecker = permissionChecker;
    }

    /// <inheritdoc />
    public async Task<AgentResolution> ResolveAgentAsync(Guid? agentId, string? provider, string? model, List<string>? toolGroups, CancellationToken ct)
    {
        var defaultProvider = provider ?? _options.Value.DefaultProvider;

        // 1. 优先使用 AgentId（加载已定义的 Agent）
        if (agentId.HasValue)
        {
            var entity = await _agentRepository.GetAsync(agentId.Value, ct);
            if (entity == null)
            {
                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentNotFound);
            }
            if (!entity.IsEnabled)
            {
                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentDisabled);
            }

            var entityToolGroups = string.IsNullOrWhiteSpace(entity.ToolGroups)
                ? null
                : JsonSerializer.Deserialize<List<string>>(entity.ToolGroups);

            var userPermissions = await ResolveUserPermissionsAsync(entityToolGroups, ct);

            // 渲染 Agent Instructions 模板变量（{{date}}, {{user.name}} 等）
            var renderedInstructions = _templateEngine.Render(
                entity.Instructions ?? string.Empty,
                new Dictionary<string, string> { ["agent.name"] = entity.Name });

            // ExternalCli 模式不需要 AgentExecutor — 跳过 ChatClient 创建
            if (entity.ExecutionMode == AgentExecutionMode.ExternalCli)
            {
                return AgentResolution.SuccessWithoutExecutor(
                    entity.Provider, model ?? entity.Model, agentId,
                    entity.Configuration, entity.ExecutionMode);
            }

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
                agentId: entity.Id,
                ct: ct);
            var creationParams = new AgentCreationParameters(renderedInstructions, entity.Name, entityToolGroups, entity.Temperature, entity.MaxTokens, userPermissions);
            return AgentResolution.Success(executor, entity.Provider, effectiveModel, agentId, entity.Configuration, entity.ExecutionMode, creationParams);
        }

        // 2. 使用 ToolGroups（无 AgentId 但有工具组）
        if (toolGroups != null && toolGroups.Count > 0)
        {
            var userPermissions = await ResolveUserPermissionsAsync(toolGroups, ct);
            var executor = await _agentFactory.CreateAgentAsync(defaultProvider, model, null, null, toolGroups, options: null, userPermissions: userPermissions, ct: ct);
            return AgentResolution.Success(executor, defaultProvider, model, null);
        }

        // 3. 仅 Provider/Model（无 AgentId 也无 ToolGroups）
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
    /// 解析当前用户已授权的工具权限集合
    /// </summary>
    private async Task<IEnumerable<string>?> ResolveUserPermissionsAsync(IEnumerable<string>? toolGroups, CancellationToken ct)
    {
        if (toolGroups == null) return null;

        if (_permissionChecker == null) return null;

        var toolDefinitions = _toolRegistry.GetToolsByGroups(toolGroups);

        // 收集所有工具声明的权限要求
        var allRequiredPermissions = toolDefinitions
            .SelectMany(t => t.RequiredPermissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allRequiredPermissions.Count == 0) return null;

        // 逐一检查权限，构建已授权集合
        var grantedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in allRequiredPermissions)
        {
            if (await _permissionChecker.IsGrantedAsync(permission))
            {
                grantedPermissions.Add(permission);
            }
        }

        return grantedPermissions;
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
