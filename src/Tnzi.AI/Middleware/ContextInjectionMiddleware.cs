namespace Tnzi.AI.Middleware;

/// <summary>
/// 上下文注入中间件 — Before only: 注入 Memory/RAG/Skills 上下文 + Persona Soul + User Profile
/// </summary>
public class ContextInjectionMiddleware : IAiMiddleware
{
    private static readonly ConcurrentDictionary<string, bool> _contextDisabledCache = new();
    /// <summary>
    /// Persona content cache, keyed by (TenantId, PersonaId). TenantId is included so the
    /// cache cannot leak content across tenants if a SuperAdmin / no-tenant code path ever
    /// populates the cache for a tenant-scoped persona. Cache eviction happens on TTL
    /// (5 minutes) AND on explicit invalidation via <see cref="InvalidatePersona(Guid)"/>
    /// (driven by AgentPersonaUpdatedEvent / AgentPersonaDeletedEvent).
    /// </summary>
    private static readonly ConcurrentDictionary<(Guid? TenantId, Guid PersonaId), (string Content, DateTime CachedAt)> _personaContentCache = new();
    /// <summary>
    /// Per-key SemaphoreSlim used to coalesce concurrent cold-start cache misses
    /// for the same (TenantId, PersonaId) — without this, N parallel requests for the
    /// same uncached persona each issue a separate DB roundtrip.
    /// </summary>
    private static readonly ConcurrentDictionary<(Guid? TenantId, Guid PersonaId), SemaphoreSlim> _personaLoadLocks = new();

    private const string SoulOpenTag = "<soul>";
    private const string SoulCloseTag = "</soul>";
    private const string UserProfileOpenTag = "<user_profile>";
    private const string UserProfileCloseTag = "</user_profile>";
    private static readonly TimeSpan PersonaCacheTtl = TimeSpan.FromMinutes(5);

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
    /// Evict all cached content for the given persona across every tenant key in the cache.
    /// Called by <c>AgentPersonaCacheInvalidationHandler</c> on AgentPersona update / delete
    /// so admin edits become visible immediately rather than at the next 5-minute TTL boundary.
    /// </summary>
    public static void InvalidatePersona(Guid personaId)
    {
        // ConcurrentDictionary.Keys is a snapshot — safe to iterate while removing.
        foreach (var key in _personaContentCache.Keys)
        {
            if (key.PersonaId == personaId)
            {
                _personaContentCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Test-only escape hatch: clear every internal cache so tests can isolate state.
    /// Not intended for production use — admin-driven invalidation should flow through
    /// <see cref="InvalidatePersona(Guid)"/> instead.
    /// </summary>
    public static void ClearAllCachesForTesting()
    {
        _personaContentCache.Clear();
        _contextDisabledCache.Clear();
        _personaLoadLocks.Clear();
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
    /// 流式路径 — Before: 注入上下文后再委托给下游
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
    /// Persona (Soul) 和 User Profile 注入与 <c>disableContextProviders</c> 开关解耦 —
    /// 该开关只控制 Memory/RAG/Skills 等动态上下文提供者，而 Persona/Profile 是 Agent 的
    /// 静态身份/用户身份信息，应当始终生效（除非显式删除 PersonaId 或 UserId）。
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
        // { "disableContextProviders": true } — skip Memory/RAG/Skills only.
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

        // 注入 Agent Persona（Soul）— 独立于 disableContextProviders 开关
        await InjectPersonaAsync(context, cancellationToken);

        // 注入 User Profile — 独立于 disableContextProviders 开关
        await InjectUserProfileAsync(context, cancellationToken);

        // Mark injection as done so retries do not duplicate context.
        context.Properties[injectedKey] = true;
    }

    /// <summary>
    /// 注入 Agent 人格（Soul）到系统消息。
    ///
    /// 优先级（任一命中即注入）：
    ///   1. AgentResolution.PersonaContent — 内联内容（workspace PERSONA.md 等场景，无需 DB）
    ///   2. AgentResolution.PersonaId — DB Persona FK，走 IAgentPersonaService（带 5min TTL 缓存 + 事件失效）
    /// </summary>
    private async Task InjectPersonaAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Inline content — workspace PERSONA.md path: short-circuit, no DB lookup
            var inlineContent = context.Agent.PersonaContent;
            if (!string.IsNullOrWhiteSpace(inlineContent))
            {
                context.Messages.Insert(0, new ChatMessage(ChatRole.System, BuildBlock(SoulOpenTag, SoulCloseTag, inlineContent)));
                _logger.LogDebug("Injected inline persona soul for agent {AgentId}", context.Agent.AgentId);
                return;
            }

            // 2. PersonaId from AgentResolution — the canonical DB path.
            // Treat Guid.Empty as "unset" — legacy snapshot rows / pre-normalization Clone
            // paths can carry Guid.Empty which would otherwise produce N wasted DB roundtrips.
            var personaId = context.Agent.PersonaId;
            if (personaId == null || personaId == Guid.Empty) return;

            var personaService = context.ServiceProvider.GetService<IAgentPersonaService>();
            if (personaService == null) return;

            var tenantId = context.ServiceProvider.GetService<ICurrentTenant>()?.Id;
            var content = await ResolvePersonaContentAsync(personaService, tenantId, personaId.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(content)) return;

            context.Messages.Insert(0, new ChatMessage(ChatRole.System, BuildBlock(SoulOpenTag, SoulCloseTag, content)));
            _logger.LogDebug("Injected persona soul for agent {AgentId} (personaId={PersonaId})", context.Agent.AgentId, personaId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject persona, continuing without soul");
        }
    }

    /// <summary>
    /// Load persona content via IAgentPersonaService with a 5-minute TTL cache
    /// keyed by (TenantId, PersonaId). Concurrent cache misses are coalesced via
    /// a per-key SemaphoreSlim so only one DB roundtrip happens per stale-cache
    /// window, regardless of inbound request concurrency.
    /// </summary>
    private static async Task<string?> ResolvePersonaContentAsync(
        IAgentPersonaService personaService, Guid? tenantId, Guid personaId, CancellationToken cancellationToken)
    {
        var key = (tenantId, personaId);

        // Fast path: cache hit within TTL
        if (TryGetFresh(key, out var fast))
            return fast;

        // Slow path: serialize concurrent misses on the same key
        var sem = _personaLoadLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        try
        {
            // Double-check: another caller may have populated the cache while we waited
            if (TryGetFresh(key, out var doubled))
                return doubled;

            var personaResult = await personaService.GetByIdAsync(personaId, cancellationToken);
            if (personaResult.Succeeded && !string.IsNullOrWhiteSpace(personaResult.Data?.Content))
            {
                _personaContentCache[key] = (personaResult.Data.Content, DateTime.UtcNow);
                return personaResult.Data.Content;
            }
            return null;
        }
        finally
        {
            sem.Release();
        }
    }

    private static bool TryGetFresh((Guid? TenantId, Guid PersonaId) key, out string? content)
    {
        if (_personaContentCache.TryGetValue(key, out var cached)
            && (DateTime.UtcNow - cached.CachedAt) < PersonaCacheTtl)
        {
            content = cached.Content;
            return true;
        }
        content = null;
        return false;
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
    /// - "disableContextProviders": true — 禁用 Memory/RAG/Skills 等动态上下文 (Persona/Profile 不受影响)
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
    /// — visually similar for the LLM when consumed as plain text, but no longer matches
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
