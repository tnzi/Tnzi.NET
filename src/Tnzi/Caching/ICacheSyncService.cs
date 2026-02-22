
namespace Tnzi.Caching;

/// <summary>
/// 缓存同步服务接口（用于分布式缓存同步）
/// </summary>
public interface ICacheSyncService
{
    /// <summary>
    /// 发布缓存失效通知
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="operation">操作类型（Remove, Update等）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishCacheInvalidationAsync(string key, CacheOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅缓存失效通知
    /// </summary>
    /// <param name="handler">处理回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SubscribeCacheInvalidationAsync(Func<string, CacheOperation, Task> handler, CancellationToken cancellationToken = default);
}

/// <summary>
/// 缓存操作类型
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// 删除
    /// </summary>
    Remove = 1,

    /// <summary>
    /// 更新
    /// </summary>
    Update = 2,

    /// <summary>
    /// 清空
    /// </summary>
    Clear = 3
}