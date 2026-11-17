namespace Tnzi.AI.Services;

/// <summary>
/// 线程标题：第一条消息被接受时写回退标题并请求 AI 标题，后台生成 AI 标题。
/// </summary>
/// <remarks>
/// 内建路径（<c>ThreadTitleMiddleware</c>，输入守卫与用量上限之后）与外部 CLI 路径（入队成功之后）都调
/// <see cref="StartAsync"/>，所以两条路径起标题的时机与规则一致。
/// 所有标题写入都带「标题来源」条件：用户改过名（<see cref="ThreadTitleSource.User"/>）的线程不会被自动标题覆盖。
/// </remarks>
public interface IThreadTitleService
{
    /// <summary>
    /// 新线程的第一条用户消息被接受：写回退标题；开了自动标题时发 <see cref="Events.ThreadTitleRequestedEvent"/>。
    /// </summary>
    /// <remarks>不等回复、不调模型；失败只记日志不抛（标题不能让这一轮失败）。</remarks>
    Task StartAsync(ThreadTitleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成 AI 标题并写入；模型失败、被截断、要调用工具或输出清洗后为空时保留回退标题。
    /// </summary>
    /// <returns>写下了 AI 标题时返回 true。</returns>
    Task<bool> GenerateAsync(Events.ThreadTitleRequestedEvent request, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IThreadTitleService.StartAsync"/> 的输入。
/// </summary>
/// <param name="ThreadId">新线程</param>
/// <param name="UserMessage">第一条用户消息：模型实际收到的文本（输入守卫改写之后）</param>
/// <param name="UserId">发起这一轮的用户</param>
/// <param name="AgentId">线程所属的 Agent</param>
public sealed record ThreadTitleRequest(Guid ThreadId, string UserMessage, Guid? UserId, Guid? AgentId);
