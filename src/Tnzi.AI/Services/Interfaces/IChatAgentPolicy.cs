namespace Tnzi.AI.Services;

/// <summary>
/// 决定一个用户能不能在聊天端点上与某个 Agent 对话（或不带 Agent 对话）。
/// </summary>
/// <remarks>
/// <para>
/// 默认实现读 <c>AI:Chat:AllowedAgentIds</c> 与 <c>AI:Chat:AllowAgentlessChat</c>（都不配 = 不限，与此前行为相同）。
/// 需要按用户、按角色、按租户决定的消费方注册自己的实现即可替换它（默认实现以 <c>TryAdd</c> 注册）。
/// </para>
/// <para>
/// 由 <c>ChatService</c> 在聊天、流式聊天、编辑与重新生成之前调用。在服务层而不是控制器里，
/// 因为聊天控制器是可被消费应用整体替换的默认控制器。它只回答「能不能对话」，Agent 是否存在、是否启用、
/// 线程归属仍由执行路径各自判定。
/// </para>
/// </remarks>
public interface IChatAgentPolicy
{
    /// <summary>
    /// 允许时返回成功；不允许时返回带状态码与错误码的失败（默认实现：403 <c>AI_CHAT_AGENT_NOT_ALLOWED</c>）。
    /// </summary>
    /// <param name="agentId">请求的 Agent；null = 不带 Agent 的聊天。</param>
    /// <param name="userId">发起聊天的用户；匿名调用为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result> CheckAsync(Guid? agentId, Guid? userId, CancellationToken cancellationToken = default);
}
