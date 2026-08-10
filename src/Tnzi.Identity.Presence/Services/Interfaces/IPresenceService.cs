namespace Tnzi.Identity.Presence.Services;

public interface IPresenceService
{
    /// <summary>设置本人手动状态意图（Offline 回落 Online；隐身被禁用时返回 403）。</summary>
    Task<Result> SetStatusAsync(UserPresenceStatus status);

    /// <summary>获取本人手动状态意图。</summary>
    Task<UserPresenceStatus> GetMyStatusAsync();

    /// <summary>批量解析一组用户的有效在线状态（意图 + 连接状态 + auto-away 综合）。</summary>
    Task<IReadOnlyList<UserPresenceDto>> ResolveEffectiveAsync(IReadOnlyCollection<Guid> userIds);

    /// <summary>
    /// 本人上报活动/空闲（auto-away 心跳）。<paramref name="active"/> 为 true 表示恢复活动，
    /// false 表示客户端越过本地空闲阈值。仅在有效状态发生变化时才推送。
    /// </summary>
    Task<Result> ReportActivityAsync(bool active);

    /// <summary>
    /// 标记用户上线活动（清 IsAutoAway、置 LastActivityAt）。连接建立时由事件处理器调用。
    /// 返回<b>旁观者视角的状态是否发生变化</b>——隐身用户恒为 <see langword="false"/>，
    /// 调用方据此跳过推送（见 <see cref="MarkOfflineAsync"/> 的说明）。
    /// </summary>
    Task<bool> MarkActiveAsync(Guid userId);

    /// <summary>
    /// 标记用户离线（写 <c>LastSeenAt</c>）。全部连接断开时由事件处理器调用。
    /// </summary>
    /// <returns>
    /// 旁观者视角的状态是否发生变化。<b>隐身用户恒为 <see langword="false"/></b>：他们在旁观者眼里
    /// 早已是离线，真的断开时既不该改动 <c>LastSeenAt</c>，也不该广播一条"状态变了"——
    /// 那两件事都会泄露"这个显示为离线的人其实刚刚才真的离开"。
    /// </returns>
    Task<bool> MarkOfflineAsync(Guid userId);

    /// <summary>解析该用户有效状态并发布 <c>UserPresenceChangedEvent</c>，触发实时推送。</summary>
    Task NotifyChangedAsync(Guid userId);
}
