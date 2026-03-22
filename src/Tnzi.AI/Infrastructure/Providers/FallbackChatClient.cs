using System.Runtime.CompilerServices;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 降级 ChatClient — 主提供商失败时按顺序尝试备选提供商
/// </summary>
/// <remarks>
/// 实现 IChatClient 的装饰器模式，在主客户端调用失败时自动降级到备选客户端。
/// 支持非流式和流式两种调用模式。
/// </remarks>
internal sealed class FallbackChatClient : IChatClient
{
    private readonly IChatClient _primary;
    private readonly IReadOnlyList<IChatClient> _fallbacks;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<IChatClient> _allClients;

    public FallbackChatClient(IChatClient primary, IReadOnlyList<IChatClient> fallbacks, ILogger logger)
    {
        _primary = Check.NotNull(primary);
        _fallbacks = Check.NotNull(fallbacks);
        _logger = Check.NotNull(logger);
        var allClients = new List<IChatClient>(_fallbacks.Count + 1) { _primary };
        allClients.AddRange(_fallbacks);
        _allClients = allClients;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // 非流式：依次尝试主客户端和降级客户端
        var allClients = _allClients;
        for (var i = 0; i < allClients.Count; i++)
        {
            var isLast = i == allClients.Count - 1;
            try
            {
                var response = await allClients[i].GetResponseAsync(chatMessages, options, cancellationToken);
                if (i > 0)
                {
                    _logger.LogInformation("Fallback provider #{Index} succeeded (non-streaming)", i);
                }
                return response;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !isLast)
            {
                _logger.LogWarning(ex, "Provider #{Index} failed (non-streaming), trying next. Error: {Error}", i, ex.Message);
            }
        }

        // 不可达，但编译器需要
        throw new InvalidOperationException("All providers failed");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return StreamWithFallbackAsync(chatMessages, options, cancellationToken);
    }

    /// <summary>
    /// 真流式降级：chunk 直接 yield 转发，仅在出错且未输出任何 chunk 时才降级到下一个 provider
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithFallbackAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var allClients = _allClients;

        for (var i = 0; i < allClients.Count; i++)
        {
            var isLast = i == allClients.Count - 1;
            var hasYielded = false;
            var cleanFail = false;

            await using var enumerator = allClients[i]
                .GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !isLast && !hasYielded)
                {
                    // 尚未输出任何 chunk — 可安全降级到下一个 provider
                    _logger.LogWarning(ex, "Provider #{Index} streaming failed before any output, trying next. Error: {Error}", i, ex.Message);
                    cleanFail = true;
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && hasYielded)
                {
                    // 已输出部分 chunk — 无法干净回退，让异常传播
                    _logger.LogWarning(ex, "Provider #{Index} streaming failed after partial output, cannot fall back", i);
                    throw;
                }

                if (!moved) break;

                hasYielded = true;
                yield return enumerator.Current;
            }

            if (!cleanFail)
            {
                // 当前 provider 成功完成
                if (i > 0)
                {
                    _logger.LogInformation("Fallback provider #{Index} streaming succeeded", i);
                }
                yield break;
            }
            // else: 继续尝试下一个 provider
        }

        throw new InvalidOperationException("All providers failed");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _primary.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _primary.Dispose();
        foreach (var fallback in _fallbacks)
        {
            fallback.Dispose();
        }
    }
}
