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

        var result = await next(context, cancellationToken);

        // After: 轮后回调（记忆沉淀、实体提炼）。下游抛出 = 这一轮没有答复，没有可提炼的东西
        await NotifyContextCompletedAsync(context, result.FinishReason, result.Response);
        return result;
    }

    /// <summary>
    /// 流式路径 - Before: 注入上下文后再委托给下游；After: 用累积的回复通知上下文提供器
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Before: 注入上下文（与非流式路径相同逻辑）
        await InjectContextAsync(context, cancellationToken);

        var reply = new StringBuilder();
        string? finishReason = null;
        var completed = false;
        try
        {
            await foreach (var chunk in next(context, cancellationToken))
            {
                if (chunk.Text != null)
                {
                    reply.Append(chunk.Text);
                }
                if (chunk.FinishReason != null)
                {
                    finishReason = chunk.FinishReason;
                }
                yield return chunk;
            }
            completed = true;
        }
        finally
        {
            // 看到终止 chunk 就算这一轮已经答完：消费方写出 [DONE] 后客户端会立刻断开，
            // 之后剩下的排空可能因请求取消而中断，但答复本身已经交付，记忆照样要提炼
            if (completed || finishReason != null)
            {
                await NotifyContextCompletedAsync(context, finishReason, reply.ToString());
            }
        }
    }

    /// <summary>
    /// 轮后回调（<see cref="IContextProvider.OnCompletedAsync"/>）的时限。回调是一到数次辅助 LLM 调用，
    /// 不再受请求令牌约束，必须有自己的上限，挂住的提炼不能无限期占着这次运行。
    /// </summary>
    internal static readonly TimeSpan CompletionTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 把这一轮交给上下文提供器做轮后处理（记忆沉淀、实体提炼）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 提供器拿到的是<b>完成的这一轮</b>：已加载的历史 + 本轮用户消息（守卫放行后的文本）+ 本轮答复。
    /// <c>context.Messages</c> 里没有后两者：本轮用户消息只在执行器的工作副本里，答复也从不写回。
    /// 拿它提炼，新线程首轮什么都提炼不到，之后每一轮提炼的都是上一轮，每段对话的最后一轮永远不进记忆。
    /// </para>
    /// <para>
    /// 令牌不用请求的：流式下客户端收到 [DONE] 就取消读取、请求随即中止，此时提炼的 LLM 调用还在途，
    /// 跟着取消就等于流式对话几乎从不沉淀记忆。与写历史、记用量、结算配额同理，改用独立的、有上限的令牌。
    /// 被拒、失败类结局不回调（判据与写历史相同，见 <see cref="FinishReasons.EndsWithAnswer"/>）。
    /// </para>
    /// </remarks>
    private async Task NotifyContextCompletedAsync(AiMiddlewareContext context, string? finishReason, string? reply)
    {
        if (!context.Properties.TryGetValue(ContextProviderKey, out var raw)
            || raw is not CompositeContextProvider { ProviderCount: > 0 } provider
            || !FinishReasons.EndsWithAnswer(finishReason))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(CompletionTimeout);
            await provider.OnCompletedAsync(BuildCompletedTurn(context, reply), timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Context provider OnCompletedAsync failed");
        }
    }

    /// <summary>
    /// 完成的这一轮：交给提供器的输入（历史 + 本轮用户消息）再加上本轮答复。新列表，不改 <c>context.Messages</c>。
    /// </summary>
    private static List<ChatMessage> BuildCompletedTurn(AiMiddlewareContext context, string? reply)
    {
        var turn = BuildProviderInput(context);
        if (!string.IsNullOrWhiteSpace(reply))
        {
            turn.Add(new ChatMessage(ChatRole.Assistant, reply));
        }
        return turn;
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
                SkillSlugs = context.Agent.SkillSlugs,
                // Resolved (and ownership-checked) by HistoryMiddleware (Order=300) before we run.
                ThreadId = context.Request.ThreadId
            });
            if (compositeProvider is not null)
            {
                try
                {
                    var injection = await compositeProvider.GetContextAsync(BuildProviderInput(context), context, cancellationToken);

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
    /// 交给上下文提供器的消息：已加载的历史 + 本轮用户消息（副本，不改 <see cref="AiMiddlewareContext.Messages"/>）。
    /// </summary>
    /// <remarks>
    /// 本轮用户消息要到 AgentRuntime 构建模型输入时才追加（见 <see cref="UserTurn"/>），此刻的
    /// <c>context.Messages</c> 里只有历史。RAG 检索、记忆召回都按「最新一条用户消息」取查询：
    /// 不补上它，新线程首轮什么都检索不到，之后每一轮都拿上一轮的问题去检索。
    /// 文本取守卫放行（含脱敏改写）之后的那一份，与模型收到的一致。
    /// </remarks>
    private static List<ChatMessage> BuildProviderInput(AiMiddlewareContext context)
    {
        var input = new List<ChatMessage>(context.Messages);
        var turnText = UserTurn.EffectiveText(context);
        if (string.IsNullOrWhiteSpace(turnText))
        {
            return input;
        }

        // 调用方已经把本轮消息放进来了（自定义管线 / 直接构造上下文）就不再重复追加
        var last = input.Count > 0 ? input[^1] : null;
        if (last is not null && last.Role == ChatRole.User && string.Equals(last.Text, turnText, StringComparison.Ordinal))
        {
            return input;
        }

        input.Add(new ChatMessage(ChatRole.User, turnText));
        return input;
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
    /// 是否为本中间件注入的 Agent 人格块（Soul）。
    /// </summary>
    /// <remarks>
    /// 人格属于注入它的那个 Agent：Handoff / Router 把对话交给另一个 Agent 时要把它去掉，
    /// 否则目标 Agent 收到的第一条系统消息是转出方的身份，与目标自己的指令相互竞争（以转出方的口吻作答）。
    /// 用户画像（user_profile）描述的是用户而不是 Agent，不在此列。
    /// </remarks>
    internal static bool IsPersonaBlock(ChatMessage message)
        => message.Role == ChatRole.System
           && message.Text is { } text
           && text.StartsWith(SoulOpenTag, StringComparison.Ordinal)
           && text.EndsWith(SoulCloseTag, StringComparison.Ordinal);

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
