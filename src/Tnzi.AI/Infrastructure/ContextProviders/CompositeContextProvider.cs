
namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 组合上下文提供器 - 将多个 IContextProvider 组合成一个
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 IContextProvider 接口，用于组合多个子 provider 的上下文。
/// 在 GetContextAsync 中，依次调用所有子 provider 并合并它们的 ContextInjection。
/// 在 OnCompletedAsync 中，依次通知所有子 provider 调用完成。
/// </para>
/// <para>
/// 默认情况下不包含任何子 provider，返回空的 ContextInjection，这确保了向后兼容性。
/// 通过配置启用后，可以添加 Memory、RAG、Skills 等子 provider。
/// </para>
/// </remarks>
public sealed class CompositeContextProvider : IContextProvider
{
    private readonly List<IContextProvider> _providers = [];
    private readonly ILogger<CompositeContextProvider> _logger;
    private readonly IOptions<AIOptions> _options;
    private readonly ITokenEstimator _tokenEstimator;

    /// <summary>
    /// 初始化 CompositeContextProvider
    /// </summary>
    public CompositeContextProvider(
        ILogger<CompositeContextProvider> logger,
        IOptions<AIOptions> options,
        ITokenEstimator? tokenEstimator = null)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
        _tokenEstimator = tokenEstimator ?? new HeuristicTokenEstimator();
    }

    /// <summary>
    /// 获取当前注册的子 provider 数量
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <summary>
    /// 添加子 provider
    /// </summary>
    /// <param name="provider">要添加的 IContextProvider</param>
    public void AddProvider(IContextProvider provider)
    {
        Check.NotNull(provider);
        _providers.Add(provider);
        _logger.LogDebug("Added context provider: {ProviderType}", provider.GetType().Name);
    }

    /// <summary>
    /// 移除子 provider
    /// </summary>
    /// <param name="provider">要移除的 IContextProvider</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveProvider(IContextProvider provider)
    {
        Check.NotNull(provider);
        var removed = _providers.Remove(provider);
        if (removed)
        {
            _logger.LogDebug("Removed context provider: {ProviderType}", provider.GetType().Name);
        }
        return removed;
    }

    /// <summary>
    /// 清除所有子 provider
    /// </summary>
    public void ClearProviders()
    {
        _providers.Clear();
        _logger.LogDebug("Cleared all context providers");
    }

    /// <summary>
    /// 在 Agent 执行前获取上下文注入
    /// </summary>
    /// <remarks>
    /// 依次调用所有子 provider 的 GetContextAsync，并合并它们的 ContextInjection：
    /// - Messages: 合并到同一个列表
    /// - Tools: 合并到同一个列表
    /// </remarks>
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        // 如果没有子 provider，返回空的 ContextInjection
        if (_providers.Count == 0)
        {
            return ContextInjection.Empty;
        }

        var mergedMessages = new List<ChatMessage>();
        var mergedTools = new List<AITool>();
        var mergedCitations = new List<CitationDto>();

        var maxBudget = _options.Value.ContextProviders.MaxTokenBudget;
        var usedTokens = 0;

        foreach (var provider in _providers)
        {
            // Token 预算检查：当预算已耗尽时跳过后续 Provider
            if (maxBudget > 0 && usedTokens >= maxBudget)
            {
                _logger.LogDebug(
                    "Token budget exhausted ({UsedTokens}/{MaxBudget}), skipping {ProviderType}",
                    usedTokens, maxBudget, provider.GetType().Name);
                break;
            }

            try
            {
                var injection = await provider.GetContextAsync(messages, ct);

                if (injection.Messages is { Count: > 0 })
                {
                    // 估算本次注入的 token 数
                    if (maxBudget > 0)
                    {
                        var injectionTokens = EstimateMessageTokens(injection.Messages);
                        if (usedTokens + injectionTokens > maxBudget)
                        {
                            _logger.LogDebug(
                                "Skipping {ProviderType} context ({InjectionTokens} tokens) — would exceed budget ({UsedTokens}+{InjectionTokens}>{MaxBudget})",
                                provider.GetType().Name, injectionTokens, usedTokens, injectionTokens, maxBudget);
                            continue;
                        }
                        usedTokens += injectionTokens;
                    }

                    mergedMessages.AddRange(injection.Messages);
                }

                if (injection.Tools is { Count: > 0 })
                {
                    mergedTools.AddRange(injection.Tools);
                }

                if (injection.Citations is { Count: > 0 })
                {
                    mergedCitations.AddRange(injection.Citations);
                }
            }
            catch (Exception ex)
            {
                // 单个 provider 失败不应影响其他 provider
                _logger.LogWarning(ex, "Context provider {ProviderType} failed during GetContextAsync", provider.GetType().Name);
            }
        }

        if (maxBudget > 0)
        {
            _logger.LogDebug("Context injection used {UsedTokens}/{MaxBudget} token budget", usedTokens, maxBudget);
        }

        return new ContextInjection
        {
            Messages = mergedMessages.Count > 0 ? mergedMessages : null,
            Tools = mergedTools.Count > 0 ? mergedTools : null,
            Citations = mergedCitations.Count > 0 ? mergedCitations : null
        };
    }

    /// <summary>
    /// 估算一组 ChatMessage 的 token 数
    /// </summary>
    private int EstimateMessageTokens(List<ChatMessage> messages)
    {
        var totalTokens = 0;
        foreach (var msg in messages)
        {
            if (msg.Text is { } text)
            {
                totalTokens += _tokenEstimator.Estimate(text, baseOverhead: 0);
            }
        }
        return totalTokens;
    }

    /// <summary>
    /// 在 Agent 执行后回调
    /// </summary>
    /// <remarks>
    /// 依次调用所有子 provider 的 OnCompletedAsync，通知它们调用已完成。
    /// 单个 provider 失败不会影响其他 provider 的执行。
    /// </remarks>
    public async Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (_providers.Count == 0)
        {
            return;
        }

        foreach (var provider in _providers)
        {
            try
            {
                await provider.OnCompletedAsync(messages, ct);
            }
            catch (Exception ex)
            {
                // 单个 provider 失败不应影响其他 provider
                _logger.LogWarning(ex, "Context provider {ProviderType} failed during OnCompletedAsync", provider.GetType().Name);
            }
        }
    }
}
