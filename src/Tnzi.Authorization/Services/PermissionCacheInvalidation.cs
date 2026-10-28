namespace Tnzi.Authorization.Services;

/// <summary>
/// 权限写路径之后的每用户权限缓存失效：此刻做一遍，身处事务时提交后再做一遍；
/// 做不成就清空整个权限缓存，连清空也失败就抛出。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>只在写入之后、提交之前清一遍不够。</b>在工作单元里（宿主开了全局工作单元，或写路径自己的
/// <c>ExecuteInUnitOfWorkAsync</c>），写入要到提交才对别的连接可见。这之间另一个请求缓存未命中，
/// 读到提交前的旧授权并回填缓存，之后再没有任何东西失效它：被撤销的码在缓存期（30 分钟）里照样放行。
/// 提交后再清一次关掉这个窗口。此刻那一遍保留：不在事务里时写入已经自动提交，它就是提交后的那一遍。
/// </para>
/// <para>
/// ★★★ <b>失效失败不能吞成一条警告然后报成功。</b>撤销已经写库、缓存还是旧集合，所有实例在缓存期内继续放行，
/// 而管理员看到的是 200。退一步清空整个权限缓存（宽但正确）；连清空也失败就让异常冒出去：
/// 在事务里它让写入回滚（失败关闭），不在事务里请求以 5xx 结束，而不是一张假的成功回执。
/// 提交后那一遍的失败只能记录（事务已经提交，提交后队列逐个隔离并记录），它补的是并发窗口，不是第一道失效。
/// </para>
/// </remarks>
internal static class PermissionCacheInvalidation
{
    /// <summary>执行一次失效；身处事务时把同一次失效再排进提交后队列。</summary>
    /// <param name="cache">权限缓存。</param>
    /// <param name="invalidate">失效动作（按人删除、清目录或全清）。必须只依赖调用前已经算好的数据：提交后那一遍不再查库。</param>
    /// <param name="unitOfWorkManager">工作单元管理器；为空或不在事务里时只做此刻那一遍。</param>
    /// <param name="postCommitQueue">提交后队列。</param>
    /// <param name="logger">记录回退。</param>
    /// <param name="scope">日志里描述失效范围的一句话。</param>
    internal static async Task RunAsync(
        FunctionAuthCache cache,
        Func<FunctionAuthCache, Task> invalidate,
        IUnitOfWorkManager? unitOfWorkManager,
        IPostCommitActionQueue? postCommitQueue,
        ILogger logger,
        string scope)
    {
        Check.NotNull(cache);
        Check.NotNull(invalidate);
        Check.NotNull(logger);

        await RunOnceAsync(cache, invalidate, logger, scope);

        if (unitOfWorkManager?.IsEnabledTransaction == true && postCommitQueue != null)
        {
            postCommitQueue.Enqueue(_ => RunOnceAsync(cache, invalidate, logger, scope));
        }
    }

    private static async Task RunOnceAsync(FunctionAuthCache cache, Func<FunctionAuthCache, Task> invalidate, ILogger logger, string scope)
    {
        try
        {
            await invalidate(cache);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invalidating the permission cache for {Scope} failed; clearing the whole permission cache instead.", scope);
            await cache.ClearAllAsync();
        }
    }
}
