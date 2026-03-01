namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 记忆上下文提供器 — 从 IMemoryStore 读取记忆并注入为 System 消息
/// </summary>
public sealed class MemoryContextProvider : IContextProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly string _scope;
    private readonly ILogger<MemoryContextProvider> _logger;
    private string? _cachedMemory;

    public MemoryContextProvider(IMemoryStore memoryStore, string scope, ILogger<MemoryContextProvider> logger)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _scope = Check.NotNullOrWhiteSpace(scope);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            // 仅加载一次，后续返回缓存结果
            _cachedMemory ??= await _memoryStore.ReadAsync(_scope, ct);

            if (string.IsNullOrWhiteSpace(_cachedMemory))
            {
                return ContextInjection.Empty;
            }

            _logger.LogDebug("Injecting memory context for scope {Scope}, length: {Length}", _scope, _cachedMemory.Length);

            var contextMessage = new ChatMessage(ChatRole.System,
                $"## Persistent Memory\nThe following is your persistent memory for scope '{_scope}':\n\n{_cachedMemory}");

            return new ContextInjection
            {
                Messages = [contextMessage]
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load memory context for scope {Scope}", _scope);
            return ContextInjection.Empty;
        }
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
