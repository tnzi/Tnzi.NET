namespace Tnzi.Feature.Events;

/// <summary>
/// 功能值被写入或删除时，精确失效 <see cref="FeatureValueCache"/> 里对应作用域的那一条。
/// </summary>
/// <remarks>
/// 事件携带的 provider 名 / provider 键 / 功能名正好是缓存键的三段，所以这里不需要按前缀扫，
/// 也不需要知道有多少个 provider。事件在写库成功之后才发（<c>FeatureService</c> 两条写路径同序），
/// 因此失效之后的下一次读取拿到的一定是新值。
/// </remarks>
public class FeatureValueCacheInvalidationHandler :
    IEventHandler<FeatureValueChangedEvent>,
    IEventHandler<FeatureValueDeletedEvent>
{
    private readonly FeatureValueCache _cache;

    /// <summary>
    /// 初始化处理器。
    /// </summary>
    public FeatureValueCacheInvalidationHandler(FeatureValueCache cache)
    {
        _cache = Check.NotNull(cache);
    }

    /// <inheritdoc />
    public Task HandleAsync(FeatureValueChangedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        return _cache.InvalidateAsync(@event.ProviderName, @event.ProviderKey, @event.FeatureName);
    }

    /// <inheritdoc />
    public Task HandleAsync(FeatureValueDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        return _cache.InvalidateAsync(@event.ProviderName, @event.ProviderKey, @event.FeatureName);
    }
}
