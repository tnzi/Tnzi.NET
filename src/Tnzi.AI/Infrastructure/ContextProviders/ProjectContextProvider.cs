namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 项目上下文注入器 — 将项目指令注入到 Agent 对话中
/// </summary>
public class ProjectContextProvider : IContextProvider
{
    private readonly IProjectContextLoader _contextLoader;
    private readonly ILogger<ProjectContextProvider> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    // 缓存已加载的指令（项目指令不会频繁变化）
    private volatile string? _cachedInstructions;
    private volatile bool _loaded;

    public ProjectContextProvider(IProjectContextLoader contextLoader, ILogger<ProjectContextProvider> logger)
    {
        _contextLoader = Check.NotNull(contextLoader);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (!_loaded)
        {
            await _loadLock.WaitAsync(ct);
            try
            {
                // Double-check after acquiring lock
                if (!_loaded)
                {
                    try
                    {
                        var context = await _contextLoader.LoadAsync(ct: ct);
                        _cachedInstructions = context.Instructions;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to load project context");
                    }
                    _loaded = true;
                }
            }
            finally
            {
                _loadLock.Release();
            }
        }

        if (string.IsNullOrEmpty(_cachedInstructions))
        {
            return ContextInjection.Empty;
        }

        // 注入为系统消息
        var systemMessage = new ChatMessage(
            ChatRole.System,
            $"# Project Instructions\n\n{_cachedInstructions}");

        return new ContextInjection
        {
            Messages = [systemMessage]
        };
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
