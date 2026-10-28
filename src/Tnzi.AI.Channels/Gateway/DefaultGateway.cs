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

            var (threadId, fromStore) = await ResolveThreadIdAsync(request, binding, threadStore);

            AgentRunResult result;
            try
            {
                result = await runtime.RunAsync(NewRunRequest(binding, request, threadId), ct);
            }
            catch (Exception ex) when (fromStore && StaleThreadMapping.IsRejected(ex))
            {
                await StaleThreadMapping.ForgetAsync(threadStore, request.Channel, request.ChatId, request.TopicId, threadId!.Value, _logger);
                threadId = null;
                result = await runtime.RunAsync(NewRunRequest(binding, request, threadId), ct);
            }

            await PersistNewThreadIdAsync(threadId, result.ThreadId, request, threadStore);

            // 追踪活跃会话
            TrackSession(binding, result.ThreadId, request);

            return new GatewayResponse
            {
                Success = true,
                // Outbound to a channel peer - deliverable, not narration (see AgentRunResult).
                Response = result.EffectiveDeliverable,
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

        // 线程查找在运行之前、受保护的枚举之外：存储故障同样要以错误终止块结束（不能在 catch 里 yield，先记下再发），
        // 否则异常穿出迭代器，拆掉正在枚举它的那条 WebSocket 连接。
        Guid? threadId = null;
        var fromStore = false;
        var lookupFailed = false;
        try
        {
            (threadId, fromStore) = await ResolveThreadIdAsync(request, binding, threadStore);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Gateway ProcessStreamingAsync thread lookup failed for channel={Channel} chatId={ChatId}",
                request.Channel, request.ChatId);
            lookupFailed = true;
        }

        if (lookupFailed)
        {
            yield return new GatewayStreamChunk { IsFinal = true, FinishReason = "error" };
            yield break;
        }

        var runRequest = NewRunRequest(binding, request, threadId);

        // 线程 id 取自 runRequest 本身：ThreadResolutionMiddleware 把新建 / 校验过的线程写回
        // context.Request.ThreadId，而那正是这里传进去的同一个对象（ChatService 也这样取）。
        // 运行时从不在流块里携带线程 id，只看流块的话新会话永远拿不到 id、映射永远写不进去，
        // 每条 chat.send 都开一条新线程、历史全丢。
        Guid? resultThreadId = threadId;
        var failed = false;

        // 流式节流：合并快速到达的 token 增量，最多每 StreamingThrottleMs 推送一次，
        // 避免刷爆 IM 平台的编辑/限流阈值。首个 token 与最终消息始终立即下发。
        var throttle = TimeSpan.FromMilliseconds(Math.Max(0, _options.CurrentValue.StreamingThrottleMs));
        var pending = new StringBuilder();
        var lastEmit = Stopwatch.GetTimestamp();
        var hasEmitted = false;

        // 手动枚举：迭代器里不能在带 catch 的 try 中 yield，而失败要在这里接住：
        // 映射指向的线程被拒时换新线程重试一次，其它失败以错误终止块结束（与 ProcessAsync 一样只记日志、不回显详情），
        // 而不是让异常穿出去拆掉整条 WebSocket 连接。
        var stream = runtime.RunStreamingAsync(runRequest, ct).GetAsyncEnumerator(ct);
        try
        {
            var received = false;
            while (true)
            {
                bool hasNext;
                Exception? failure = null;
                try
                {
                    hasNext = await stream.MoveNextAsync();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    hasNext = false;
                }

                if (failure != null)
                {
                    // 线程解析在任何输出之前完成：一个块都没发出时才可能是它，也才能无痕重试。
                    if (!received && fromStore && StaleThreadMapping.IsRejected(failure))
                    {
                        await stream.DisposeAsync();
                        await StaleThreadMapping.ForgetAsync(threadStore, request.Channel, request.ChatId, request.TopicId, threadId!.Value, _logger);
                        threadId = null;
                        resultThreadId = null;
                        fromStore = false;
                        runRequest = NewRunRequest(binding, request, threadId);
                        stream = runtime.RunStreamingAsync(runRequest, ct).GetAsyncEnumerator(ct);
                        continue;
                    }

                    _logger.LogError(failure, "Gateway ProcessStreamingAsync failed for channel={Channel} chatId={ChatId}",
                        request.Channel, request.ChatId);
                    failed = true;
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                received = true;
                var chunk = stream.Current;
                resultThreadId = runRequest.ThreadId ?? resultThreadId;

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
        }
        finally
        {
            await stream.DisposeAsync();
        }

        if (failed)
        {
            // 与 ProcessAsync 一致：失败不登记映射、不追踪会话。已收到但尚未下发的文本随终止块一起发出。
            yield return new GatewayStreamChunk
            {
                TextDelta = pending.Length > 0 ? pending.ToString() : null,
                IsFinal = true,
                ThreadId = resultThreadId,
                FinishReason = "error"
            };
            yield break;
        }

        resultThreadId = runRequest.ThreadId ?? resultThreadId;

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

        // 回复此时已经全部下发：映射写不进去只意味着下一条消息开新线程，记日志即可，
        // 不让异常在终止块之后穿出去拆掉连接。
        try
        {
            await PersistNewThreadIdAsync(threadId, resultThreadId, request, threadStore);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Gateway ProcessStreamingAsync could not record the thread mapping for channel={Channel} chatId={ChatId}",
                request.Channel, request.ChatId);
        }

        TrackSession(binding, resultThreadId, request);
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

    /// <inheritdoc />
    public Task<IReadOnlyList<GatewaySession>> GetPeerSessionsAsync(string peerId, string? agentId = null)
    {
        Check.NotNullOrWhiteSpace(peerId);

        IReadOnlyList<GatewaySession> sessions = _activeSessions.Values
            .Where(s => OwnedBy(s, peerId))
            .Where(s => agentId == null || s.AgentId?.ToString() == agentId)
            .ToList()
            .AsReadOnly();

        return Task.FromResult(sessions);
    }

    /// <inheritdoc />
    public Task<GatewaySession?> GetPeerSessionAsync(string peerId, string sessionKey)
    {
        Check.NotNullOrWhiteSpace(peerId);
        Check.NotNullOrWhiteSpace(sessionKey);

        _activeSessions.TryGetValue(sessionKey, out var session);
        return Task.FromResult(session != null && OwnedBy(session, peerId) ? session : null);
    }

    /// <inheritdoc />
    public Task<bool> PrunePeerSessionAsync(string peerId, string sessionKey)
    {
        Check.NotNullOrWhiteSpace(peerId);
        Check.NotNullOrWhiteSpace(sessionKey);

        // 先确认归属再删：ConcurrentDictionary 的 TryRemove(key) 不看值，会删掉任何一条键匹配的行。
        if (!_activeSessions.TryGetValue(sessionKey, out var session) || !OwnedBy(session, peerId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_activeSessions.TryRemove(sessionKey, out _));
    }

    /// <summary>
    /// 会话归属判定：<see cref="GatewaySession.OwnerPeerId"/> 是 <c>TrackSession</c> 写入的
    /// <c>GatewayRequest.UserId</c>，由服务端解析（已认证连接 = 用户 id，匿名连接 = 匿名命名空间下的连接 id，
    /// 见 <c>GatewayWebSocketHandler.ResolvePeerId</c>），与调用 session.* 的那一方用的是同一个标识，因此是可信的归属键。
    /// 序数比较：peer 标识是标识符不是自然语言。
    /// </summary>
    private static bool OwnedBy(GatewaySession session, string peerId)
        => string.Equals(session.OwnerPeerId, peerId, StringComparison.Ordinal);

    private SessionBinding ResolveBinding(GatewayRequest request)
    {
        var context = new SessionBindingContext
        {
            Channel = request.Channel,
            ChatId = request.ChatId,
            UserId = request.UserId,
            PeerKind = request.PeerKind,
            ExplicitAgentId = request.AgentId?.ToString(),
            FallbackAgentId = request.FallbackAgentId,
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

    private void TrackSession(SessionBinding binding, Guid? threadId, GatewayRequest request)
    {
        _activeSessions[binding.SessionKey] = new GatewaySession
        {
            SessionKey = binding.SessionKey,
            AgentId = binding.AgentId,
            ThreadId = threadId,
            Channel = request.Channel,
            PeerId = request.ChatId,
            OwnerPeerId = request.UserId,
            LastActivityAt = DateTimeOffset.UtcNow,
            // 服务端解析的归属租户（GatewayRequest.TenantId 不接受客户端 JSON），供管理端按租户筛选
            TenantId = request.TenantId
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
    /// 解析线程 ID：请求显式指定 > 绑定已有 > 从 thread store 查找。
    /// <c>FromStore</c> 标出 id 是不是映射里存的那条：只有它被拒时才该删映射、换新线程重试，
    /// 调用方显式给的线程被拒是调用方的错，原样失败。
    /// </summary>
    private static async Task<(Guid? ThreadId, bool FromStore)> ResolveThreadIdAsync(
        GatewayRequest request, SessionBinding binding, IChannelThreadStore threadStore)
    {
        var explicitThreadId = request.ThreadId ?? binding.ThreadId;
        if (explicitThreadId != null)
        {
            return (explicitThreadId, false);
        }

        var stored = await threadStore.GetThreadIdAsync(request.Channel, request.ChatId, request.TopicId);
        return (stored, stored != null);
    }

    /// <remarks>
    /// <see cref="AgentRunRequest.UserId"/> 只取服务端认证过的框架用户（<see cref="GatewayRequest.AuthenticatedUserId"/>），
    /// 绝不取 <see cref="GatewayRequest.UserId"/>（IM 渠道上那是平台账号）。<c>QuotaMiddleware</c> 只看它：留空时
    /// 预算门与按用户配额都跳过，已登录用户换到 WebSocket 网关就能绕过自己的额度。
    /// </remarks>
    private static AgentRunRequest NewRunRequest(SessionBinding binding, GatewayRequest request, Guid? threadId)
        => new()
        {
            AgentId = binding.AgentId,
            UserMessage = request.UserMessage,
            ThreadId = threadId,
            UserId = request.AuthenticatedUserId
        };

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
