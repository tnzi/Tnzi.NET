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
    /// 移除映射（线程删除时清理）
    /// </summary>
    Task RemoveAsync(string channelName, string chatId, string? topicId = null);
}
