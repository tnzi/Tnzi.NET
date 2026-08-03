namespace Tnzi.AI.Channels.Manager;

/// <summary>
/// 消息调度管理器 - 消费入站消息，路由命令/聊天，调用 AI，发布出站回复。
/// 支持并发控制和流式更新节流。
/// </summary>
public class ChannelManager : IChannelManager
{
    private readonly ILogger<ChannelManager> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelsModuleOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private readonly Dictionary<string, IChannelAdapter> _adapterMap;
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
                await _concurrency.WaitAsync(ct);
                // CancellationToken.None 防止 Task.Run 在 ct 取消时跳过 action 导致信号量泄漏
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessMessageAsync(message, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled error processing message from {Channel}:{ChatId}",
                            message.ChannelName, message.ChatId);
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, CancellationToken.None);
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
        var command = message.Text.Trim().ToLowerInvariant();

        switch (command)
        {
            case "/new" or "/start":
            {
                var createResult = await threadService.CreateAsync(new CreateAgentThreadDto
                {
                    AgentId = _options.DefaultAgentId,
                    Title = $"Channel: {message.ChannelName}"
                });

                if (!createResult.Succeeded)
                {
                    await PublishErrorReplyAsync(message, "Failed to create a new conversation. Please try again.");
                    return;
                }

                var threadId = createResult.Data!.Id;
                await threadStore.SetThreadIdAsync(message.ChannelName, message.ChatId, threadId, message.TopicId, message.UserId);
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, threadId,
                    "New conversation started. How can I help you?"));
                break;
            }
            case "/status":
            {
                var threadId = await threadStore.GetThreadIdAsync(message.ChannelName, message.ChatId, message.TopicId);
                var statusText = threadId != null
                    ? $"Active thread: {threadId:N}"
                    : "No active conversation. Send /new to start one.";
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, threadId ?? Guid.Empty, statusText));
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
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, Guid.Empty, modelsText));
                break;
            }
            case "/memory":
            {
                var threadId = await threadStore.GetThreadIdAsync(message.ChannelName, message.ChatId, message.TopicId);
                var memoryStore = scopedProvider.GetService<IMemoryStore>();
                string memoryText;
                if (memoryStore != null && threadId != null)
                {
                    var entries = await memoryStore.SearchAsync("default", query: "", maxResults: 5, ct: ct);
                    memoryText = entries.Count > 0
                        ? $"Memory ({entries.Count} entries):\n" + string.Join("\n",
                            entries.Select(e => $"• [{e.Category ?? "general"}] {e.Content.Truncate(80)}"))
                        : "No memory entries found for current context.";
                }
                else
                {
                    memoryText = threadId == null
                        ? "No active conversation. Send /new to start one."
                        : "Memory service is not available.";
                }
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, threadId ?? Guid.Empty, memoryText));
                break;
            }
            case "/help":
            {
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, Guid.Empty,
                    "Commands:\n/new - Start a new conversation\n/status - Show current thread\n/models - List available AI models\n/memory - Show memory entries\n/help - Show this message"));
                break;
            }
            default:
            {
                await _bus.PublishOutboundAsync(new OutboundMessage(
                    message.ChannelName, message.ChatId, Guid.Empty,
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
        // 优先尝试通过 Gateway 处理（统一路由 + 会话绑定）
        if (await TryHandleChatViaGatewayAsync(message, ct))
        {
            return;
        }

        // 回退到直接 IAgentRuntime 调用
        var threadId = await threadStore.GetThreadIdAsync(message.ChannelName, message.ChatId, message.TopicId);

        var request = new AgentRunRequest
        {
            AgentId = _options.DefaultAgentId,
            UserMessage = message.Text,
            ThreadId = threadId
        };

        var result = await runtime.RunAsync(request, ct);

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
        await _bus.PublishOutboundAsync(new OutboundMessage(
            message.ChannelName, message.ChatId, actualThreadId,
            result.EffectiveDeliverable,
            IsFinal: true));
    }

    /// <summary>
    /// 尝试通过 Gateway 处理聊天消息，成功返回 true，失败或不可用返回 false（回退到直接 Runtime）
    /// </summary>
    private async Task<bool> TryHandleChatViaGatewayAsync(InboundMessage message, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var gateway = scope.ServiceProvider.GetService<IGateway>();
            if (gateway == null) return false;

            var request = new GatewayRequest
            {
                Channel = message.ChannelName,
                ChatId = message.ChatId,
                UserId = message.UserId ?? "unknown",
                TopicId = message.TopicId,
                UserMessage = message.Text,
                AgentId = _options.DefaultAgentId,
                // 按消息来源渠道解析归属租户，供 Gateway 做绑定规则租户分区 + 处理作用域租户上下文
                TenantId = ResolveChannelTenantId(message.ChannelName)
            };

            var response = await gateway.ProcessAsync(request, ct);

            if (!response.Success)
            {
                _logger.LogWarning("Gateway returned error for {Channel}:{ChatId}: {Error}, falling back to direct runtime",
                    message.ChannelName, message.ChatId, response.Error);
                return false;
            }

            var threadId = response.ThreadId ?? Guid.Empty;
            await _bus.PublishOutboundAsync(new OutboundMessage(
                message.ChannelName, message.ChatId, threadId,
                response.Response ?? string.Empty,
                IsFinal: true));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway processing failed for {Channel}:{ChatId}, falling back to direct runtime",
                message.ChannelName, message.ChatId);
            return false;
        }
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

    private async Task PublishErrorReplyAsync(InboundMessage message, string errorText)
    {
        try
        {
            await _bus.PublishOutboundAsync(new OutboundMessage(
                message.ChannelName, message.ChatId, Guid.Empty, errorText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish error reply to {Channel}:{ChatId}", message.ChannelName, message.ChatId);
        }
    }
}
