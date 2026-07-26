namespace Tnzi.AI.Middleware;

/// <summary>
/// 上下文注入中间件 - Before only: 注入 Memory/RAG/Skills 上下文 + Persona Soul + User Profile
/// </summary>
public class ContextInjectionMiddleware : IAiMiddleware
{
    private static readonly ConcurrentDictionary<string, bool> _contextDisabledCache = new();

    private const string SoulOpenTag = "<soul>";
    private const string SoulCloseTag = "</soul>";
    private const string UserProfileOpenTag = "<user_profile>";
    private const string UserProfileCloseTag = "</user_profile>";

    private readonly CompositeContextProviderFactory _providerFactory;
    private readonly ILogger<ContextInjectionMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ContextInjection;

    public ContextInjectionMiddleware(
        CompositeContextProviderFactory providerFactory,
        ILogger<ContextInjectionMiddleware> logger)
    {
        _providerFactory = Check.NotNull(providerFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Property key used to stash the per-request CompositeContextProvider so the
    /// post-completion hook can run after downstream pipeline finishes.
    /// </summary>
    internal const string ContextProviderKey = "__ContextInjectionMiddleware.Provider";

    /// <summary>
    /// Test-only escape hatch: clear the context-disabled cache so tests can isolate state.
    /// </summary>
    public static void ClearAllCachesForTesting()
    {
        _contextDisabledCache.Clear();
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        // Before: 注入上下文
        await InjectContextAsync(context, cancellationToken);
        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            await NotifyContextCompletedAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// 流式路径 - Before: 注入上下文后再委托给下游
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Before: 注入上下文（与非流式路径相同逻辑）
        await InjectContextAsync(context, cancellationToken);

        try
        {
            await foreach (var chunk in next(context, cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            await NotifyContextCompletedAsync(context, cancellationToken);
        }
    }

    private async Task NotifyContextCompletedAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        if (context.Properties.TryGetValue(ContextProviderKey, out var raw)
            && raw is CompositeContextProvider provider
            && provider.ProviderCount > 0)
        {
            try
            {
                await provider.OnCompletedAsync(context.Messages, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Context provider OnCompletedAsync failed");
            }
        }
    }

    /// <summary>
    /// 共享的上下文注入逻辑。
    ///
    /// Persona (Soul) 和 User Profile 注入与 <c>disableContextProviders</c> 开关解耦 -
    /// 该开关只控制 Memory/RAG/Skills 等动态上下文提供者，而 Persona/Profile 是 Agent 的
    /// 静态身份/用户身份信息，应当始终生效（除非 Agent 无 Persona 内容或无 UserId）。
    /// </summary>
    private async Task InjectContextAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        // Idempotency guard: RetryMiddleware (Order=100) wraps the whole inner pipeline and
        // re-invokes it on transient failures.  Without this guard each retry would call
        // InjectContextAsync again on the SAME mutable context, duplicating <soul>/
        // <user_profile> inserts and appending citations multiple times.
        const string injectedKey = "__ContextInjectionMiddleware.Injected";
        if (context.Properties.ContainsKey(injectedKey))
        {
            _logger.LogDebug("Context injection already ran for this context; skipping on retry");
            return;
        }

        // Per-agent context provider control via Agent.Configuration JSON:
        // { "disableContextProviders": true } - skip Memory/RAG/Skills only.
        // Persona soul and user profile are deliberately injected outside this guard.
        if (!IsContextDisabledForAgent(context))
        {
            // Contributors are scoped factories that need per-agent context (AgentId/UserId)
            // to decide whether to participate, so we cannot pre-build this in DI.
            var compositeProvider = _providerFactory.TryBuild(new ContextProviderCreationContext
            {
                AgentId = context.Agent.AgentId,
                AgentName = context.Agent.Agent?.Name,
                UserId = context.Request.UserId,
                // Per-agent resource assignments → scope RAG retrieval + skill visibility at runtime.
                KnowledgeBaseIds = context.Agent.KnowledgeBaseIds,
                SkillSlugs = context.Agent.SkillSlugs
            });
            if (compositeProvider is not null)
            {
                try
                {
                    var injection = await compositeProvider.GetContextAsync(context.Messages, context, cancellationToken);

                    if (injection.Messages is { Count: > 0 })
                    {
                        // 在消息列表头部插入上下文消息（系统消息之后）
                        var insertIndex = context.Messages.FindIndex(m => m.Role != ChatRole.System);
                        if (insertIndex < 0) insertIndex = context.Messages.Count;
                        context.Messages.InsertRange(insertIndex, injection.Messages);
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

                    // Propagate the composite provider into context so the post-completion
                    // hook (OnCompletedAsync) can run after the agent finishes.
                    context.Properties[ContextProviderKey] = compositeProvider;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Context injection failed, continuing without context");
                }
            }
        }
        else
        {
            _logger.LogDebug("Memory/RAG/Skills context injection disabled for agent {AgentId} via Configuration (persona/profile still apply)", context.Agent.AgentId);
        }

        // 注入 Agent Persona（Soul）- 独立于 disableContextProviders 开关
        await InjectPersonaAsync(context, cancellationToken);

        // 注入 User Profile - 独立于 disableContextProviders 开关
        await InjectUserProfileAsync(context, cancellationToken);

        // Mark injection as done so retries do not duplicate context.
        context.Properties[injectedKey] = true;
    }

    /// <summary>
    /// 注入 Agent 人格（Soul）到系统消息。内容来自 <see cref="AgentResolution.PersonaContent"/> -
    /// DB agent 的内联 <see cref="Entities.Agent.Persona"/> 列，或 workspace PERSONA.md 正文。
    /// 内容随 Agent 一起解析，无 DB 二次查询、无缓存、无事件失效。
    /// </summary>
    private Task InjectPersonaAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        try
        {
            var content = context.Agent.PersonaContent;
            if (string.IsNullOrWhiteSpace(content)) return Task.CompletedTask;

            context.Messages.Insert(0, new ChatMessage(ChatRole.System, BuildBlock(SoulOpenTag, SoulCloseTag, content)));
            _logger.LogDebug("Injected persona soul for agent {AgentId}", context.Agent.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject persona, continuing without soul");
        }
        return Task.CompletedTask;
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

            var profileBody = string.Join("\n", parts);
            context.Messages.Insert(0, new ChatMessage(ChatRole.System, BuildBlock(UserProfileOpenTag, UserProfileCloseTag, profileBody)));
            _logger.LogDebug("Injected user profile for user {UserId}", userId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject user profile, continuing without it");
        }
    }

    /// <summary>
    /// 检查 Agent.Configuration 是否禁用了 Memory/RAG/Skills 上下文注入。
    /// </summary>
    /// <remarks>
    /// 支持的 Configuration JSON 字段：
    /// - "disableContextProviders": true - 禁用 Memory/RAG/Skills 等动态上下文 (Persona/Profile 不受影响)
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

    /// <summary>
    /// Build a system-message block <c>&lt;tag&gt;content&lt;/tag&gt;</c>, neutralizing any
    /// occurrence of either the opening or the closing tag inside the body so authored
    /// content cannot break out of the wrapper and inject sibling pseudo-system blocks.
    ///
    /// Replacement strategy inserts a single space inside each tag
    /// (<c>&lt;soul&gt;</c> → <c>&lt; soul&gt;</c>, <c>&lt;/soul&gt;</c> → <c>&lt;/ soul&gt;</c>)
    /// - visually similar for the LLM when consumed as plain text, but no longer matches
    /// the boundary token an attacker would use to forge a sibling system block.
    /// </summary>
    private static string BuildBlock(string openTag, string closeTag, string content)
    {
        // "<soul>"  → "< soul>"  (space after "<")
        var safeOpen = string.Concat(openTag.AsSpan(0, 1), " ", openTag.AsSpan(1));
        // "</soul>" → "</ soul>" (space after "</")
        var safeClose = string.Concat(closeTag.AsSpan(0, 2), " ", closeTag.AsSpan(2));
        var sanitized = content
            .Replace(closeTag, safeClose, StringComparison.OrdinalIgnoreCase)
            .Replace(openTag, safeOpen, StringComparison.OrdinalIgnoreCase);
        return string.Concat(openTag, sanitized, closeTag);
    }
}
