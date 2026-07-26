using System.Runtime.CompilerServices;

namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认 Gateway 实现 - 路由请求到 Agent，追踪活跃会话
/// </summary>
public class DefaultGateway : IGateway
{
    private readonly ISessionBinder _binder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultGateway> _logger;
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly ConcurrentDictionary<string, GatewaySession> _activeSessions = new();

    public DefaultGateway(ISessionBinder binder, IServiceScopeFactory scopeFactory, IOptionsMonitor<GatewayOptions> options, ILogger<DefaultGateway> logger)
    {
        _binder = Check.NotNull(binder);
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);
        EvictStaleSessions();

        try
        {
            var binding = ResolveBinding(request);

            if (binding.AgentId == Guid.Empty)
            {
                return new GatewayResponse { Success = false, Error = "No agent configured for this binding." };
            }

            using var scope = _scopeFactory.CreateScope();
            // 在 Gateway 处理作用域内建立租户上下文（请求归属租户为 null 时不切换，行为不变）
            using var tenantScope = ChangeTenantScope(scope.ServiceProvider, request.TenantId);
            var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
            var threadStore = scope.ServiceProvider.GetRequiredService<IChannelThreadStore>();

            var threadId = await ResolveThreadIdAsync(request, binding, threadStore);

            var runRequest = new AgentRunRequest
            {
                AgentId = binding.AgentId,
                UserMessage = request.UserMessage,
                ThreadId = threadId
            };

            var result = await runtime.RunAsync(runRequest, ct);

            await PersistNewThreadIdAsync(threadId, result.ThreadId, request, threadStore);

            // 追踪活跃会话
            TrackSession(binding, result.ThreadId, request.Channel, request.ChatId);

            return new GatewayResponse
            {
                Success = true,
                Response = result.Response,
                ThreadId = result.ThreadId
            };
        }
        catch (Exception ex)
        {
            // Do not echo internal exception details to the caller - log them server-side,
            // return a generic error (parity with ProcessStreamingAsync).
            _logger.LogError(ex, "Gateway ProcessAsync failed for channel={Channel} chatId={ChatId}",
                request.Channel, request.ChatId);
            return new GatewayResponse { Success = false, Error = "error" };
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GatewayStreamChunk> ProcessStreamingAsync(
        GatewayRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNull(request);
        EvictStaleSessions();

        SessionBinding? binding = null;
        string? bindingError = null;

        try
        {
            binding = ResolveBinding(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway ProcessStreamingAsync binding resolution failed");
            bindingError = "error";
        }

        if (bindingError != null)
        {
            yield return new GatewayStreamChunk { IsFinal = true, FinishReason = "error" };
            yield break;
        }

        if (binding!.AgentId == Guid.Empty)
        {
            yield return new GatewayStreamChunk { TextDelta = "No agent configured for this binding.", IsFinal = true, FinishReason = "error" };
            yield break;
        }

        using var scope = _scopeFactory.CreateScope();
        // 在 Gateway 处理作用域内建立租户上下文（请求归属租户为 null 时不切换，行为不变）
        using var tenantScope = ChangeTenantScope(scope.ServiceProvider, request.TenantId);
        var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
        var threadStore = scope.ServiceProvider.GetRequiredService<IChannelThreadStore>();

        var threadId = await ResolveThreadIdAsync(request, binding, threadStore);

        var runRequest = new AgentRunRequest
        {
            AgentId = binding.AgentId,
            UserMessage = request.UserMessage,
            ThreadId = threadId
        };

        Guid? resultThreadId = null;

        // 流式节流：合并快速到达的 token 增量，最多每 StreamingThrottleMs 推送一次，
        // 避免刷爆 IM 平台的编辑/限流阈值。首个 token 与最终消息始终立即下发。
        var throttle = TimeSpan.FromMilliseconds(Math.Max(0, _options.CurrentValue.StreamingThrottleMs));
        var pending = new StringBuilder();
        var lastEmit = Stopwatch.GetTimestamp();
        var hasEmitted = false;

        await foreach (var chunk in runtime.RunStreamingAsync(runRequest, ct).WithCancellation(ct))
        {
            resultThreadId ??= chunk.EventData?.TryGetValue("ThreadId", out var tid) == true && tid is Guid g ? g : null;

            if (chunk.Text is { Length: > 0 })
            {
                pending.Append(chunk.Text);
            }

            var isFinal = chunk.FinishReason != null;

            // 立即下发的条件：终止块 / 首块（快速首字）/ 距上次下发已达节流间隔；否则继续合并
            if (isFinal || !hasEmitted || Stopwatch.GetElapsedTime(lastEmit) >= throttle)
            {
                yield return new GatewayStreamChunk
                {
                    TextDelta = pending.ToString(),
                    IsFinal = isFinal,
                    ThreadId = resultThreadId,
                    FinishReason = chunk.FinishReason
                };
                pending.Clear();
                lastEmit = Stopwatch.GetTimestamp();
                hasEmitted = true;
            }
        }

        // 冲刷残留：流在无 FinishReason 情况下结束且仍有未跨过节流阈值的缓冲文本
        // （保持原语义：非终止块 IsFinal=false，枚举结束即为真正的终止信号）
        if (pending.Length > 0)
        {
            yield return new GatewayStreamChunk
            {
                TextDelta = pending.ToString(),
                IsFinal = false,
                ThreadId = resultThreadId,
                FinishReason = null
            };
        }

        await PersistNewThreadIdAsync(threadId, resultThreadId, request, threadStore);

        TrackSession(binding, resultThreadId, request.Channel, request.ChatId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GatewaySession>> GetSessionsAsync(string? agentId = null)
    {
        IReadOnlyList<GatewaySession> sessions = agentId == null
            ? _activeSessions.Values.ToList().AsReadOnly()
            : _activeSessions.Values
                .Where(s => s.AgentId?.ToString() == agentId)
                .ToList().AsReadOnly();

        return Task.FromResult(sessions);
    }

    /// <inheritdoc />
    public Task<GatewaySession?> GetSessionAsync(string sessionKey)
    {
        Check.NotNullOrWhiteSpace(sessionKey);
        _activeSessions.TryGetValue(sessionKey, out var session);
        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public Task PruneSessionAsync(string sessionKey)
    {
        Check.NotNullOrWhiteSpace(sessionKey);
        _activeSessions.TryRemove(sessionKey, out _);
        return Task.CompletedTask;
    }

    private SessionBinding ResolveBinding(GatewayRequest request)
    {
        var context = new SessionBindingContext
        {
            Channel = request.Channel,
            ChatId = request.ChatId,
            UserId = request.UserId,
            PeerKind = request.PeerKind,
            ExplicitAgentId = request.AgentId?.ToString(),
            // 透传渠道归属租户 - 带 TenantId 的绑定规则按租户分区命中（null = 部署级全局）
            TenantId = request.TenantId
        };

        return _binder.Resolve(context);
    }

    /// <summary>
    /// 在给定作用域内切换当前租户上下文；tenantId 为 null 时不做任何事（返回 null 供 using 安全释放）。
    /// 使 IChannelThreadStore 等作用域服务的多租户审计填充/全局过滤生效。
    /// </summary>
    private static IDisposable? ChangeTenantScope(IServiceProvider scopedProvider, Guid? tenantId)
        => tenantId.HasValue
            ? scopedProvider.GetService<ICurrentTenant>()?.Change(tenantId)
            : null;

    private void TrackSession(SessionBinding binding, Guid? threadId, string channel, string peerId)
    {
        _activeSessions[binding.SessionKey] = new GatewaySession
        {
            SessionKey = binding.SessionKey,
            AgentId = binding.AgentId,
            ThreadId = threadId,
            Channel = channel,
            PeerId = peerId,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 懒驱逐 - 清理超过 SessionEvictionHours 未活跃的会话
    /// </summary>
    private void EvictStaleSessions()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(_options.CurrentValue.SessionEvictionHours);
        var stale = _activeSessions
            .Where(kvp => kvp.Value.LastActivityAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in stale)
        {
            _activeSessions.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 解析线程 ID：请求显式指定 > 绑定已有 > 从 thread store 查找
    /// </summary>
    private async Task<Guid?> ResolveThreadIdAsync(GatewayRequest request, SessionBinding binding, IChannelThreadStore threadStore)
    {
        return request.ThreadId ?? binding.ThreadId
            ?? await threadStore.GetThreadIdAsync(request.Channel, request.ChatId, request.TopicId);
    }

    /// <summary>
    /// 保存新线程映射（仅当 runtime 返回了新线程 ID 时）
    /// </summary>
    private async Task PersistNewThreadIdAsync(Guid? resolvedThreadId, Guid? resultThreadId, GatewayRequest request, IChannelThreadStore threadStore)
    {
        if (resolvedThreadId == null && resultThreadId != null)
        {
            await threadStore.SetThreadIdAsync(request.Channel, request.ChatId, resultThreadId.Value, request.TopicId, request.UserId);
        }
    }
}
