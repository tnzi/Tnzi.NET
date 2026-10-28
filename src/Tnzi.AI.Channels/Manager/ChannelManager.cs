namespace Tnzi.AI.Channels.Manager;

/// <summary>
/// 消息调度管理器 - 消费入站消息，路由命令/聊天，调用 AI，发布出站回复。
/// 支持并发控制和流式更新节流。
/// </summary>
/// <remarks>
/// <b>同一会话（渠道 + 聊天 + 话题）的消息按到达顺序逐条处理，不同会话之间并发</b>（上限 <c>MaxConcurrency</c>）。
/// 用户连发两条消息是常态（「总结一下」紧跟一句更正）：并发处理时两次运行读到同一份历史、各自答各自的，
/// 回复按完成先后而不是提问先后送达；新会话的两条首发消息各建一条线程、映射后写者胜，前一条线程连同它的消息成了孤儿。
/// <para>
/// 每个会话一条「车道」：会话没有在处理的消息时，新消息占一个并发槽、起一个工作者；工作者处理完手上这条，
/// 就接着处理同一车道里排队的下一条，车道空了才归还槽位。所以一个连发消息的会话最多只占一个槽，挤不掉别的会话。
/// </para>
/// </remarks>
public class ChannelManager : IChannelManager
{
    private readonly ILogger<ChannelManager> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelsModuleOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private readonly Dictionary<string, IChannelAdapter> _adapterMap;

    /// <summary>正在处理中的会话 → 排在它后面的消息。键在、队列空 = 有一条在处理、没有排队的。</summary>
    private readonly Dictionary<string, Queue<InboundMessage>> _lanes = new(StringComparer.Ordinal);
    private readonly Lock _lanesGate = new();
    private CancellationTokenSource? _cts;
    private Task? _consumeLoop;

    public ChannelManager(
        ILogger<ChannelManager> logger,
        IChannelMessageBus bus,
        IServiceScopeFactory scopeFactory,
        IOptions<ChannelsModuleOptions> options,
        IEnumerable<IChannelAdapter>? adapters = null)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options).Value;
        _concurrency = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        // 按名称索引适配器，用于解析消息来源渠道归属的租户（TryAdd：重名时先注册者生效）
        _adapterMap = new Dictionary<string, IChannelAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters ?? [])
        {
            _adapterMap.TryAdd(adapter.Name, adapter);
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumeLoop = ConsumeLoopAsync(_cts.Token);
        _logger.LogInformation("ChannelManager started with max concurrency {Max}", _options.MaxConcurrency);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_consumeLoop != null)
            {
                try { await _consumeLoop; }
                catch (OperationCanceledException) { /* expected */ }
            }
            _cts.Dispose();
        }
        _logger.LogInformation("ChannelManager stopped");
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = await _bus.ConsumeInboundAsync(ct);
                var lane = LaneKey(message);

                // 这个会话已有消息在处理：排到它后面，由那个工作者按序处理，不另占并发槽
                if (TryJoinLane(lane, message))
                {
                    continue;
                }

                try
                {
                    await _concurrency.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // 车道是为这条消息开的，它不会被处理了；不关掉的话，同一会话之后的消息会排进一条没有工作者的车道
                    CloseLane(lane);
                    throw;
                }

                // CancellationToken.None 防止 Task.Run 在 ct 取消时跳过 action 导致信号量泄漏、车道永不关闭
                _ = Task.Run(() => DrainLaneAsync(lane, message, ct), CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in channel manager consume loop");
            }
        }
    }

    /// <summary>
    /// 一条车道的工作者：处理手上这条，再按到达顺序处理同一会话排进来的消息，车道空了才归还并发槽。
    /// </summary>
    private async Task DrainLaneAsync(string lane, InboundMessage first, CancellationToken ct)
    {
        var laneClosed = false;
        try
        {
            InboundMessage? next = first;
            while (next != null)
            {
                try
                {
                    await ProcessMessageAsync(next, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error processing message from {Channel}:{ChatId}",
                        next.ChannelName, next.ChatId);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                next = TakeNextOrCloseLane(lane);
                laneClosed = next == null;
            }
        }
        finally
        {
            if (!laneClosed)
            {
                // 停机：排着的消息与总线里还没取出的消息一样不再处理
                var dropped = CloseLane(lane);
                if (dropped > 0)
                {
                    _logger.LogInformation("Dropped {Count} queued message(s) for {Lane} on shutdown", dropped, lane);
                }
            }

            _concurrency.Release();
        }
    }

    /// <summary>会话已有工作者时把消息排进它的车道并返回 true；否则为这条消息开一条车道并返回 false。</summary>
    private bool TryJoinLane(string lane, InboundMessage message)
    {
        lock (_lanesGate)
        {
            if (_lanes.TryGetValue(lane, out var pending))
            {
                pending.Enqueue(message);
                return true;
            }

            _lanes[lane] = new Queue<InboundMessage>();
            return false;
        }
    }

    /// <summary>取车道里的下一条；没有了就在同一把锁里关掉车道（之后到达的消息会开一条新车道）。</summary>
    private InboundMessage? TakeNextOrCloseLane(string lane)
    {
        lock (_lanesGate)
        {
            var pending = _lanes[lane];
            if (pending.Count > 0)
            {
                return pending.Dequeue();
            }

            _lanes.Remove(lane);
            return null;
        }
    }

    /// <summary>关掉车道并返回被丢弃的排队消息数。</summary>
    private int CloseLane(string lane)
    {
        lock (_lanesGate)
        {
            return _lanes.Remove(lane, out var pending) ? pending.Count : 0;
        }
    }

    /// <summary>
    /// 车道键与线程映射的键同形（渠道 + 聊天 + 话题）：同一条线程上的运行一次只有一个。
    /// 用单元分隔符拼接，平台 id 里不会出现它。
    /// </summary>
    private static string LaneKey(InboundMessage message)
        => $"{message.ChannelName}\u001f{message.ChatId}\u001f{message.TopicId}";

    private async Task ProcessMessageAsync(InboundMessage message, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            // 在处理作用域内建立消息来源渠道归属的租户上下文（AsyncLocal 沿调用链传播），
            // 使 ChannelThreadMapping 等 IMultiTenant 实体的审计填充/全局过滤自然生效。
            // 渠道未归属租户（TenantId=null）时不切换，行为与单租户部署完全一致。
            using var tenantScope = ChangeTenantScope(scope.ServiceProvider, ResolveChannelTenantId(message.ChannelName));
            var threadStore = scope.ServiceProvider.GetRequiredService<IChannelThreadStore>();
            var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
            var threadService = scope.ServiceProvider.GetRequiredService<IAgentThreadService>();

            if (message.Type == InboundMessageType.Command)
            {
                await HandleCommandAsync(message, threadStore, threadService, scope.ServiceProvider, ct);
            }
            else
            {
                await HandleChatAsync(message, threadStore, runtime, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing channel message from {Channel}:{ChatId}", message.ChannelName, message.ChatId);
            await PublishErrorReplyAsync(message, "An error occurred while processing your message. Please try again.");
        }
    }

    private async Task HandleCommandAsync(
        InboundMessage message,
        IChannelThreadStore threadStore,
        IAgentThreadService threadService,
        IServiceProvider scopedProvider,
        CancellationToken ct)
    {
        var command = NormalizeCommand(message.Text);

        switch (command)
        {
            case "/new" or "/start":
            {
                // ★ 线程要为「绑定规则解析出的 Agent」建，不是渠道默认 Agent：下一条消息经 Gateway 命中规则 Agent B
                //   时会拿到这条线程，而线程归属 A 会被 AgentThreadService 以「Agent 不匹配」拒绝 —— 规则在管理端
                //   显示启用，这个 peer 却被静默改道。Telegram 客户端首次接触会自动发 /start，所以不是边角。
                var createResult = await threadService.CreateAsync(new CreateAgentThreadDto
                {
                    AgentId = ResolveBoundAgentId(message, scopedProvider),
                    Title = $"Channel: {message.ChannelName}"
                });

                if (!createResult.Succeeded)
                {
                    await PublishErrorReplyAsync(message, "Failed to create a new conversation. Please try again.");
                    return;
                }

                var threadId = createResult.Data!.Id;
                await threadStore.SetThreadIdAsync(message.ChannelName, message.ChatId, threadId, message.TopicId, message.UserId);
                await _bus.PublishOutboundAsync(Reply(message, threadId, "New conversation started. How can I help you?"));
                break;
            }
            case "/status":
            {
                var threadId = await threadStore.GetThreadIdAsync(message.ChannelName, message.ChatId, message.TopicId);
                var statusText = threadId != null
                    ? $"Active thread: {threadId:N}"
                    : "No active conversation. Send /new to start one.";
                await _bus.PublishOutboundAsync(Reply(message, threadId ?? Guid.Empty, statusText));
                break;
            }
            case "/models":
            {
                var chatClientFactory = scopedProvider.GetService<IChatClientFactory>();
                string modelsText;
                if (chatClientFactory != null)
                {
                    var providers = chatClientFactory.GetAvailableProviders();
                    var lines = providers.Select(p =>
                    {
                        var defaultModel = chatClientFactory.GetDefaultModel(p);
                        return $"• {p}: {defaultModel ?? "(no default)"}";
                    });
                    modelsText = "Available models:\n" + string.Join("\n", lines);
                }
                else
                {
                    modelsText = "Model information is not available.";
                }
                await _bus.PublishOutboundAsync(Reply(message, Guid.Empty, modelsText));
                break;
            }
            case "/help":
            {
                await _bus.PublishOutboundAsync(Reply(message, Guid.Empty, "Commands:\n/new - Start a new conversation\n/status - Show current thread\n/models - List available AI models\n/help - Show this message"));
                break;
            }
            default:
            {
                await _bus.PublishOutboundAsync(Reply(message, Guid.Empty,
                    $"Unknown command: {message.Text}. Send /help for available commands."));
                break;
            }
        }
    }

    private async Task HandleChatAsync(
        InboundMessage message,
        IChannelThreadStore threadStore,
        IAgentRuntime runtime,
        CancellationToken ct)
    {
        // 装了 Gateway 就只走 Gateway（统一路由 + 会话绑定）。它失败时**不**回退到直连 Runtime：
        // 直连路径按渠道默认 Agent 跑，等于把一个规则绑定的 peer 静默改道到别的 Agent，还把同一条消息跑第二遍；
        // 失败要告诉用户，回退只属于没装 Gateway 的部署。
        var handledByGateway = await TryHandleChatViaGatewayAsync(message, ct);
        if (handledByGateway != null)
        {
            if (handledByGateway == false)
            {
                await PublishErrorReplyAsync(message, "An error occurred while processing your message. Please try again.");
            }
            return;
        }

        // 没装 Gateway：直接 IAgentRuntime 调用
        var threadId = await threadStore.GetThreadIdAsync(message.ChannelName, message.ChatId, message.TopicId);

        AgentRunResult result;
        try
        {
            result = await runtime.RunAsync(DirectRunRequest(message, threadId), ct);
        }
        catch (Exception ex) when (threadId != null && StaleThreadMapping.IsRejected(ex))
        {
            // 映射指向的线程已删除或归属别的 Agent（与 Gateway 路径同一自愈，见 StaleThreadMapping）
            await StaleThreadMapping.ForgetAsync(threadStore, message.ChannelName, message.ChatId, message.TopicId, threadId.Value, _logger);
            threadId = null;
            result = await runtime.RunAsync(DirectRunRequest(message, threadId), ct);
        }

        // 如果是新线程，保存映射
        var actualThreadId = result.ThreadId ?? threadId ?? Guid.Empty;
        if (threadId == null && result.ThreadId != null)
        {
            await threadStore.SetThreadIdAsync(
                message.ChannelName, message.ChatId, result.ThreadId.Value, message.TopicId, message.UserId);
        }

        // Outbound goes to a real person in an IM client: send the deliverable, not the
        // running commentary. Identical to Response on the non-streaming path used here,
        // and correct on its own terms if this ever moves to streaming.
        await _bus.PublishOutboundAsync(Reply(message, actualThreadId, result.EffectiveDeliverable));
    }

    private AgentRunRequest DirectRunRequest(InboundMessage message, Guid? threadId)
        => new()
        {
            AgentId = _options.DefaultAgentId,
            UserMessage = message.Text,
            ThreadId = threadId
        };

    /// <summary>
    /// 尝试通过 Gateway 处理聊天消息：没装 Gateway 返回 null（调用方走直连 Runtime），
    /// 处理成功返回 true，Gateway 报错或抛异常返回 false（调用方回一条错误，不改道）。
    /// </summary>
    private async Task<bool?> TryHandleChatViaGatewayAsync(InboundMessage message, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var gateway = scope.ServiceProvider.GetService<IGateway>();
        if (gateway == null) return null;

        try
        {
            var request = new GatewayRequest
            {
                Channel = message.ChannelName,
                ChatId = message.ChatId,
                UserId = message.UserId ?? "unknown",
                TopicId = message.TopicId,
                PeerKind = message.PeerKind,
                UserMessage = message.Text,
                // 渠道默认 Agent 是兜底不是显式目标：填进 AgentId 会短路 Gateway 的全部绑定规则
                //（配置 + 数据库），让「slack 频道 X → Agent B」这类规则对 IM 流量静默失效。
                FallbackAgentId = _options.DefaultAgentId,
                // 按消息来源渠道解析归属租户，供 Gateway 做绑定规则租户分区 + 处理作用域租户上下文
                TenantId = ResolveChannelTenantId(message.ChannelName)
            };

            var response = await gateway.ProcessAsync(request, ct);

            if (!response.Success)
            {
                _logger.LogWarning("Gateway returned error for {Channel}:{ChatId}: {Error}",
                    message.ChannelName, message.ChatId, response.Error);
                return false;
            }

            var threadId = response.ThreadId ?? Guid.Empty;
            await _bus.PublishOutboundAsync(Reply(message, threadId, response.Response ?? string.Empty));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway processing failed for {Channel}:{ChatId}",
                message.ChannelName, message.ChatId);
            return false;
        }
    }

    /// <summary>
    /// 这个 peer 的消息会被路由到哪个 Agent：与 <see cref="IGateway"/> 用同一个 <see cref="ISessionBinder"/>、
    /// 同一份上下文（显式 Agent 为空、渠道默认为兜底、租户取自来源渠道）解析；没装绑定器（也就没装 Gateway）
    /// 时才是渠道默认 Agent。<c>Guid.Empty</c>（什么都没配）返回 null，建出无 Agent 的线程，与直连路径同形。
    /// </summary>
    private Guid? ResolveBoundAgentId(InboundMessage message, IServiceProvider scopedProvider)
    {
        var binder = scopedProvider.GetService<ISessionBinder>();
        if (binder == null)
        {
            return _options.DefaultAgentId;
        }

        var binding = binder.Resolve(new SessionBindingContext
        {
            Channel = message.ChannelName,
            ChatId = message.ChatId,
            UserId = message.UserId ?? "unknown",
            TopicId = message.TopicId,
            PeerKind = message.PeerKind,
            ExplicitAgentId = null,
            FallbackAgentId = _options.DefaultAgentId,
            TenantId = ResolveChannelTenantId(message.ChannelName)
        });

        return binding.AgentId == Guid.Empty ? null : binding.AgentId;
    }

    /// <summary>
    /// 解析消息来源渠道归属的租户 ID（来自渠道 adapter options 配置）。
    /// 未注册适配器或未配置租户时返回 null（单租户/全局行为）。
    /// </summary>
    private Guid? ResolveChannelTenantId(string channelName)
        => _adapterMap.TryGetValue(channelName, out var adapter) ? adapter.TenantId : null;

    /// <summary>
    /// 在给定作用域内切换当前租户上下文；tenantId 为 null 时不做任何事（返回 null 供 using 安全释放）。
    /// </summary>
    private static IDisposable? ChangeTenantScope(IServiceProvider scopedProvider, Guid? tenantId)
        => tenantId.HasValue
            ? scopedProvider.GetService<ICurrentTenant>()?.Change(tenantId)
            : null;

    /// <summary>
    /// 命令名归一：取第一个空白分隔的词，去掉 <c>@机器人名</c> 后缀，转小写。
    /// Telegram 群聊里从命令菜单点出的命令带机器人名（<c>/new@MyBot</c>），深链接带参数（<c>/start payload</c>），
    /// 整串比较会把这些都答成「Unknown command」。内置命令都不收参数，参数一律忽略。
    /// </summary>
    private static string NormalizeCommand(string text)
    {
        var trimmed = text.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var name = end < 0 ? trimmed : trimmed[..end];
        var at = name.IndexOf('@');
        if (at > 0)
        {
            name = name[..at];
        }

        return name.ToLowerInvariant();
    }

    /// <summary>
    /// 构造对某条入站消息的回复：把入站的线程指针（<c>ThreadTs</c> / <c>TopicId</c>）与平台元数据原样带回，
    /// 使回复落在提问所在的 Slack 线程 / Telegram 论坛话题 / Discord 交互里。
    /// 所有出站回复都必须经此构造：直接 <c>new OutboundMessage(...)</c> 会把回复投到频道顶层或 General。
    /// </summary>
    private static OutboundMessage Reply(InboundMessage source, Guid threadId, string text)
        => new(source.ChannelName, source.ChatId, threadId, text,
            ThreadTs: source.ThreadTs, Metadata: source.Metadata, TopicId: source.TopicId);

    private async Task PublishErrorReplyAsync(InboundMessage message, string errorText)
    {
        try
        {
            await _bus.PublishOutboundAsync(Reply(message, Guid.Empty, errorText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish error reply to {Channel}:{ChatId}", message.ChannelName, message.ChatId);
        }
    }
}
