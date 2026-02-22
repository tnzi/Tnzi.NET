namespace Tnzi.BackgroundJobs;

/// <summary>
/// 后台任务管理器接口
/// </summary>
public interface IBackgroundJobManager
{
    /// <summary>
    /// 将任务加入队列立即执行
    /// </summary>
    /// <typeparam name="TArgs">参数类型</typeparam>
    /// <param name="args">任务参数</param>
    /// <returns>任务ID</returns>
    string Enqueue<TArgs>(TArgs args) where TArgs : class;
    
    /// <summary>
    /// 延迟执行任务
    /// </summary>
    /// <typeparam name="TArgs">参数类型</typeparam>
    /// <param name="args">任务参数</param>
    /// <param name="delay">延迟时间</param>
    /// <returns>任务ID</returns>
    string Schedule<TArgs>(TArgs args, TimeSpan delay) where TArgs : class;
    
    /// <summary>
    /// 在指定时间执行任务
    /// </summary>
    /// <typeparam name="TArgs">参数类型</typeparam>
    /// <param name="args">任务参数</param>
    /// <param name="enqueueAt">执行时间</param>
    /// <returns>任务ID</returns>
    string Schedule<TArgs>(TArgs args, DateTimeOffset enqueueAt) where TArgs : class;
    
    /// <summary>
    /// 创建周期性任务
    /// </summary>
    /// <typeparam name="TArgs">参数类型</typeparam>
    /// <param name="jobId">任务ID（用于更新或删除）</param>
    /// <param name="args">任务参数</param>
    /// <param name="cronExpression">Cron 表达式</param>
    void CreateRecurring<TArgs>(string jobId, TArgs args, string cronExpression) where TArgs : class;
    
    /// <summary>
    /// 删除任务
    /// </summary>
    /// <param name="jobId">任务ID</param>
    /// <returns>是否成功删除</returns>
    bool Delete(string jobId);
    
    /// <summary>
    /// 删除周期性任务
    /// </summary>
    /// <param name="jobId">任务ID</param>
    void DeleteRecurring(string jobId);
}

/// <summary>
/// 后台任务接口
/// </summary>
/// <typeparam name="TArgs">参数类型</typeparam>
public interface IBackgroundJob<in TArgs>
    where TArgs : class
{
    /// <summary>
    /// 执行任务
    /// </summary>
    /// <param name="args">任务参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ExecuteAsync(TArgs args, CancellationToken cancellationToken = default);
}
