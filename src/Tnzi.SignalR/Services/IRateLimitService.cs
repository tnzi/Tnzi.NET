namespace Tnzi.SignalR.Services;

/// <summary>
/// 速率限制服务接口
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// 检查用户是否可以建立新连接
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否允许</returns>
    Task<bool> CheckConnectionLimitAsync(Guid userId);

    /// <summary>
    /// 检查用户是否可以发送消息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否允许</returns>
    Task<bool> CheckMessageRateLimitAsync(Guid userId);

    /// <summary>
    /// 记录消息发送
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>任务</returns>
    Task RecordMessageAsync(Guid userId);

    /// <summary>
    /// 检查用户是否被封禁
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否被封禁</returns>
    Task<bool> IsUserBannedAsync(Guid userId);

    /// <summary>
    /// 封禁用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="duration">封禁时长</param>
    /// <returns>任务</returns>
    Task BanUserAsync(Guid userId, TimeSpan duration);

    // ---------- 匿名分区 ----------
    //
    // 匿名连接没有用户 id，按分区键（默认客户端 IP）限流。这几个方法是**默认接口方法**，
    // 老的自定义实现不会因为新增而编译不过。
    //
    // ★ 默认实现刻意**失败关闭**（检查一律返回 false = 不允许）：一个没有实现它们的
    // 自定义 IRateLimitService 若默认放行，就等于把匿名旁路悄悄留着，而这正是这几个
    // 方法要修的东西。需要恢复旧行为时把 SignalR:RateLimit:AnonymousPolicy 配成
    // Allow —— 那是配置里看得见的一次选择，不是一个看不见的默认。

    /// <summary>
    /// 匿名分区是否还能再建立连接（允许时应已计入该连接）。
    /// </summary>
    /// <param name="partitionKey">分区键（客户端 IP 或连接 ID）</param>
    /// <returns>是否允许</returns>
    Task<bool> TryAcquireAnonymousConnectionAsync(string partitionKey) => Task.FromResult(false);

    /// <summary>
    /// 释放一个匿名连接名额（连接断开时调用）。
    /// </summary>
    /// <param name="partitionKey">分区键</param>
    /// <returns>任务</returns>
    Task ReleaseAnonymousConnectionAsync(string partitionKey) => Task.CompletedTask;

    /// <summary>
    /// 匿名分区是否可以继续发消息。
    /// </summary>
    /// <param name="partitionKey">分区键</param>
    /// <returns>是否允许</returns>
    Task<bool> CheckAnonymousMessageRateLimitAsync(string partitionKey) => Task.FromResult(false);

    /// <summary>
    /// 记录一条匿名分区的消息。
    /// </summary>
    /// <param name="partitionKey">分区键</param>
    /// <returns>任务</returns>
    Task RecordAnonymousMessageAsync(string partitionKey) => Task.CompletedTask;

    /// <summary>
    /// 匿名分区是否被封禁。
    /// </summary>
    /// <param name="partitionKey">分区键</param>
    /// <returns>是否被封禁</returns>
    Task<bool> IsAnonymousBannedAsync(string partitionKey) => Task.FromResult(true);

    /// <summary>
    /// 封禁一个匿名分区。
    /// </summary>
    /// <param name="partitionKey">分区键</param>
    /// <param name="duration">封禁时长</param>
    /// <returns>任务</returns>
    Task BanAnonymousAsync(string partitionKey, TimeSpan duration) => Task.CompletedTask;
}
