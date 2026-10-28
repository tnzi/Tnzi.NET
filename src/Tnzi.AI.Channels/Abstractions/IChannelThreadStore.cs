namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// Channel->Thread 映射存储 - 维护 IM 会话和 AI 线程的对应关系
/// </summary>
public interface IChannelThreadStore
{
    /// <summary>
    /// 获取 IM 会话对应的 AI 线程 ID
    /// </summary>
    /// <param name="channelName">适配器名称</param>
    /// <param name="chatId">IM 会话/群 ID</param>
    /// <param name="topicId">话题 ID（可选，部分平台支持群内话题）</param>
    Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null);

    /// <summary>
    /// 设置 IM 会话对应的 AI 线程 ID
    /// </summary>
    Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null);

    /// <summary>
    /// 移除一个会话的映射（映射指向的线程被运行拒绝时，由自愈路径调用）
    /// </summary>
    Task RemoveAsync(string channelName, string chatId, string? topicId = null);

    /// <summary>
    /// 移除指向某条线程的全部映射（线程被删除时，由 <c>ThreadDeletedEvent</c> 处理器调用）。
    /// </summary>
    /// <remarks>
    /// 默认实现什么都不做，自定义存储不必为此改动：指向已删线程的映射会在该会话的下一条消息上
    /// 被运行拒绝，再由自愈路径删掉并换新线程。实现它只是让清理不必等到下一条消息。
    /// </remarks>
    Task RemoveByThreadIdAsync(Guid threadId) => Task.CompletedTask;
}
