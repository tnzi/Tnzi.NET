namespace Tnzi.Chat.Services;

/// <summary>
/// 聊天使用权判定（白名单）：聊天默认关闭，仅被授予 <c>chat.use</c> 的角色/用户可用。
/// 授权走标准权限解析（<c>(RoleFunction ∪ user-allow) − user-deny</c>，超管自然放行）。
/// Authorization 模块未加载时无从判定 → fail-open（无 gate，聊天照常可用）。
/// </summary>
public interface IChatAccessService
{
    /// <summary>指定用户是否可使用聊天（是否持 <c>chat.use</c>）。</summary>
    Task<bool> CanUseAsync(Guid userId);

    /// <summary>当前登录用户是否可使用聊天。未认证 → false。</summary>
    Task<bool> CanCurrentUserUseAsync();

    /// <summary>从给定用户集中筛出「不可使用聊天」的用户（用于批量剔除被禁的消息接收方）。</summary>
    Task<IReadOnlySet<Guid>> FilterDisabledAsync(IEnumerable<Guid> userIds);
}
