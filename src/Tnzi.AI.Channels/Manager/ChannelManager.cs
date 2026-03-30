namespace Tnzi.AI.Channels.Manager;

/// <summary>
/// 消息调度管理器 — 消费入站消息，路由命令/聊天，调用 AI，发布出站回复。
/// 支持并发控制和流式更新节流。
/// </summary>
public class ChannelManager : IChannelManager
{
    private readonly ILogger<ChannelManager> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelsModuleOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private CancellationTokenSource? _cts;
    private Task? _consumeLoop;

    public ChannelManager(
        ILogger<ChannelManager> logger,
        IChannelMessageBus bus,
        IServiceScopeFactory scopeFactory,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options).Value;
        _concurrency = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
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
                    "Commands:\n/new — Start a new conversation\n/status — Show current thread\n/models — List available AI models\n/memory — Show memory entries\n/help — Show this message"));
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
        // 解析或创建线程（AgentRuntime 会自动创建线程如果 ThreadId 为空）
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

        await _bus.PublishOutboundAsync(new OutboundMessage(
            message.ChannelName, message.ChatId, actualThreadId,
            result.Response,
            IsFinal: true));
    }

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
