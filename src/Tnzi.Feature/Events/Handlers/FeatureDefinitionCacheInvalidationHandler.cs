namespace Tnzi.Feature.Events;

/// <summary>
/// 功能定义被新建、修改或删除之后，再失效一次定义快照（<see cref="IFeatureManager"/>）。
/// </summary>
/// <remarks>
/// <para>
/// <c>FeatureService</c> 写完就地失效一次，但那一刻环境工作单元（<c>AspNetCore:EnableGlobalUnitOfWork</c>）
/// 还没提交：另一个请求恰在这时刷新快照，读到的是旧的已提交行，并以新时间戳缓存整整一个
/// <c>Feature:CacheRefreshIntervalMinutes</c>。把功能关掉当紧急开关用时，开关在本实例上半小时不生效。
/// </para>
/// <para>
/// 这三个事件经事务感知发布（<c>EventBus:TransactionAwarePublish</c>，默认开）在<b>提交之后</b>才派发，
/// 所以这里的第二次失效一定落在新数据可见之后；没有环境事务时它紧跟写入执行，只是多失效一次。
/// 其它实例仍按刷新间隔追上（事件走本地总线），与值缓存同一取舍。
/// </para>
/// </remarks>
public class FeatureDefinitionCacheInvalidationHandler :
    IEventHandler<FeatureDefinitionCreatedEvent>,
    IEventHandler<FeatureDefinitionUpdatedEvent>,
    IEventHandler<FeatureDefinitionDeletedEvent>
{
    private readonly IFeatureManager _featureManager;

    /// <summary>
    /// 初始化处理器。
    /// </summary>
    public FeatureDefinitionCacheInvalidationHandler(IFeatureManager featureManager)
    {
        _featureManager = Check.NotNull(featureManager);
    }

    /// <inheritdoc />
    public Task HandleAsync(FeatureDefinitionCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        return _featureManager.InvalidateCacheAsync();
    }

    /// <inheritdoc />
    public Task HandleAsync(FeatureDefinitionUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        return _featureManager.InvalidateCacheAsync();
    }

    /// <inheritdoc />
    public Task HandleAsync(FeatureDefinitionDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        return _featureManager.InvalidateCacheAsync();
    }
}
