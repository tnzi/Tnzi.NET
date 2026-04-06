namespace Tnzi.AI.Middleware;

/// <summary>
/// 上下文注入中间件 — Before only: 注入 Memory/RAG/Skills 上下文 + Persona Soul + User Profile
/// </summary>
public class ContextInjectionMiddleware : IAiMiddleware
{
    private static readonly ConcurrentDictionary<string, bool> _contextDisabledCache = new();
    private static readonly ConcurrentDictionary<string, Guid?> _personaIdCache = new();
    private static readonly ConcurrentDictionary<Guid, (string Content, DateTime CachedAt)> _personaContentCache = new();

    private readonly CompositeContextProvider _contextProvider;
    private readonly ILogger<ContextInjectionMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ContextInjection;

    public ContextInjectionMiddleware(
        CompositeContextProvider contextProvider,
        ILogger<ContextInjectionMiddleware> logger)
    {
        _contextProvider = Check.NotNull(contextProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        // Before: 注入上下文
        await InjectContextAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    }

    /// <summary>
    /// 流式路径 — Before: 注入上下文后再委托给下游
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        // Before: 注入上下文（与非流式路径相同逻辑）
        await InjectContextAsync(context, cancellationToken);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 共享的上下文注入逻辑
    /// </summary>
    private async Task InjectContextAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        // Per-agent context provider control via Agent.Configuration JSON:
        // { "disableContextProviders": true } — skip all context injection for this agent
        if (IsContextDisabledForAgent(context))
        {
            _logger.LogDebug("Context injection disabled for agent {AgentId} via Configuration", context.Agent.AgentId);
            return;
        }

        if (_contextProvider.ProviderCount > 0)
        {
            try
            {
                var injection = await _contextProvider.GetContextAsync(context.Messages, context, cancellationToken);

                if (injection.Messages is { Count: > 0 })
                {
                    context.Messages.InsertRange(0, injection.Messages);
                    _logger.LogDebug("Injected {Count} context messages", injection.Messages.Count);
                }

                if (injection.Tools is { Count: > 0 })
                {
                    context.AdditionalTools.AddRange(injection.Tools);
                    _logger.LogDebug("Injected {Count} additional tools", injection.Tools.Count);
                }

                if (injection.Citations is { Count: > 0 })
                {
                    context.Citations.AddRange(injection.Citations);
                    _logger.LogDebug("Injected {Count} citations", injection.Citations.Count);
                }

                if (injection.ActiveSkills is { Count: > 0 })
                {
                    context.Properties["ActiveSkills"] = injection.ActiveSkills;
                    _logger.LogDebug("Propagated {Count} active skills to middleware context", injection.ActiveSkills.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Context injection failed, continuing without context");
            }
        }

        // 注入 Agent Persona（Soul）
        await InjectPersonaAsync(context, cancellationToken);

        // 注入 User Profile
        await InjectUserProfileAsync(context, cancellationToken);
    }

    /// <summary>
    /// 注入 Agent 人格（Soul）到系统消息
    /// </summary>
    private async Task InjectPersonaAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        // 仅当 Agent 关联了 PersonaId 时注入
        var personaId = GetPersonaId(context);
        if (personaId == null) return;

        try
        {
            var personaService = context.ServiceProvider.GetService<IAgentPersonaService>();
            if (personaService == null) return;

            // 缓存 Persona 内容（5 分钟 TTL，避免每次请求查库）
            string? content = null;
            if (_personaContentCache.TryGetValue(personaId.Value, out var cached)
                && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 5)
            {
                content = cached.Content;
            }
            else
            {
                var personaResult = await personaService.GetByIdAsync(personaId.Value, cancellationToken);
                if (personaResult.Succeeded && !string.IsNullOrWhiteSpace(personaResult.Data?.Content))
                {
                    content = personaResult.Data.Content;
                    _personaContentCache[personaId.Value] = (content, DateTime.UtcNow);
                }
            }

            if (string.IsNullOrWhiteSpace(content)) return;

            var soulMessage = new ChatMessage(ChatRole.System, $"<soul>{content}</soul>");
            context.Messages.Insert(0, soulMessage);
            _logger.LogDebug("Injected persona soul for agent {AgentId}", context.Agent.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject persona, continuing without soul");
        }
    }

    /// <summary>
    /// 注入用户档案到系统消息
    /// </summary>
    private async Task InjectUserProfileAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        var userId = context.Request.UserId;
        if (userId == null) return;

        try
        {
            var profileService = context.ServiceProvider.GetService<IUserProfileService>();
            if (profileService == null) return;

            var profile = await profileService.FindByUserIdAsync(userId.Value, cancellationToken);
            if (profile == null) return;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.DisplayName))
                parts.Add($"Name: {profile.DisplayName}");
            if (!string.IsNullOrWhiteSpace(profile.Role))
                parts.Add($"Role: {profile.Role}");
            if (!string.IsNullOrWhiteSpace(profile.PreferredLanguage))
                parts.Add($"Preferred Language: {profile.PreferredLanguage}");
            if (!string.IsNullOrWhiteSpace(profile.Content))
                parts.Add(profile.Content);

            if (parts.Count == 0) return;

            var profileBlock = $"<user_profile>{string.Join("\n", parts)}</user_profile>";
            context.Messages.Insert(0, new ChatMessage(ChatRole.System, profileBlock));
            _logger.LogDebug("Injected user profile for user {UserId}", userId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject user profile, continuing without it");
        }
    }

    /// <summary>
    /// 从 Agent Configuration JSON 获取 PersonaId
    /// </summary>
    private static Guid? GetPersonaId(AiMiddlewareContext context)
    {
        var config = context.Agent.AgentConfiguration;
        if (string.IsNullOrEmpty(config)) return null;

        // 按配置字符串缓存解析结果
        return _personaIdCache.GetOrAdd(config, static cfg =>
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg);
                if (doc.RootElement.TryGetProperty("personaId", out var prop)
                    && prop.ValueKind == JsonValueKind.String
                    && Guid.TryParse(prop.GetString(), out var id))
                {
                    return id;
                }
            }
            catch { /* ignore parse errors */ }
            return null;
        });
    }

    /// <summary>
    /// 检查 Agent.Configuration 是否禁用了上下文注入
    /// </summary>
    /// <remarks>
    /// 支持的 Configuration JSON 字段：
    /// - "disableContextProviders": true — 禁用所有上下文注入（Memory/RAG/Skills 等）
    /// 适用于纯工具型 Agent（如翻译、格式转换）不需要记忆和知识注入的场景。
    /// </remarks>
    private static bool IsContextDisabledForAgent(AiMiddlewareContext context)
    {
        var config = context.Agent.AgentConfiguration;
        if (string.IsNullOrEmpty(config)) return false;

        return _contextDisabledCache.GetOrAdd(config, static cfg =>
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg);
                return doc.RootElement.TryGetProperty("disableContextProviders", out var prop)
                    && prop.ValueKind == JsonValueKind.True;
            }
            catch
            {
                return false;
            }
        });
    }
}
