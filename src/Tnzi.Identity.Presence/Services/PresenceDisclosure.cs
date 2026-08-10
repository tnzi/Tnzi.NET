namespace Tnzi.Identity.Presence.Services;

/// <summary>
/// 连接生灭该在 presence 行上留下什么痕迹的纯函数。<see cref="PresenceResolver"/> 回答
/// "现在该显示成什么"，本类回答"该记下什么、该不该广播" —— 两者共同定义隐身的边界。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须是一处</b>：隐身泄露从来不是从状态字段漏出去的（<c>PresenceResolver</c> 一直
/// 老老实实把 Invisible 解析成 Offline），而是从<b>时间戳</b>和<b>广播时机</b>漏出去的。
/// 这两条都散在写路径上，各写各的就迟早有一条忘了判断。
/// </para>
/// <para>
/// ★ <b>核心不变量</b>：<c>LastSeenAt</c> 的含义是「最后一次<b>被看得见</b>的时刻」，
/// 而不是「最后一次断开连接的时刻」。对旁观者而言隐身就是下线，所以隐身那一刻即盖章，
/// 此后真的断开时什么都不该发生 —— 时间戳不动、也不广播。
/// 否则一个显示为离线的人会在他真正离开时让时间戳突然跳到此刻，
/// 把"我一直在，只是不想让你知道"变成"我刚刚才走"。
/// </para>
/// </remarks>
public static class PresenceDisclosure
{
    /// <summary>这一行此刻是否对旁观者隐藏。</summary>
    /// <remarks>
    /// 部署关掉隐身时（<c>AllowInvisible=false</c>）历史隐身意图按在线解析，因此也不算隐藏 ——
    /// 判据与 <see cref="PresenceResolver.Resolve"/> 的同名分支同源，两处必须一起改。
    /// </remarks>
    public static bool IsHidden(UserPresenceStatus intent, bool allowInvisible)
        => intent == UserPresenceStatus.Invisible && allowInvisible;

    /// <summary>
    /// 一次手动状态切换，是否让这个人从「看得见」变成「隐身」（即该不该盖 <c>LastSeenAt</c> 的章）。
    /// </summary>
    /// <param name="newIntent">用户刚选择的状态。</param>
    /// <param name="currentEffective">
    /// 切换<b>之前</b>旁观者看到的状态（<see cref="IPresenceService.ResolveEffectiveAsync"/> 的结果）。
    /// </param>
    /// <remarks>
    /// ★ <b>只看新意图是不够的</b>，这是本方法存在的全部理由：
    /// <list type="bullet">
    /// <item>已经隐身的人再点一次「隐身」，若也盖章，他的"最后在线时间"就会在隐身期间一路往前走 ——
    /// 泄露强度与完全不修相同。</item>
    /// <item>本来就没连上（旁观者已看到离线）的人切到隐身，若盖章，反而把时间戳往<b>后</b>推，
    /// 显得他刚刚还在线 —— 一个方向相反但同样真实的泄露。</item>
    /// </list>
    /// 两种情形的共同判据都是"切换前旁观者看到的是不是 Offline"，故以有效状态而非意图判定。
    /// </remarks>
    public static bool IsGoingDark(UserPresenceStatus newIntent, UserPresenceStatus currentEffective)
        => newIntent == UserPresenceStatus.Invisible
           && currentEffective != UserPresenceStatus.Offline;

    /// <summary>建立首个连接。</summary>
    /// <returns>旁观者视角是否发生变化（据此决定要不要广播）。</returns>
    public static bool ApplyConnected(UserPresence row, DateTime now, bool allowInvisible)
    {
        Check.NotNull(row);

        // 活动时间是内部记账（auto-away 用），不对外披露，隐身与否都照记。
        row.IsAutoAway = false;
        row.LastActivityAt = now;

        if (IsHidden(row.Status, allowInvisible))
            return false;

        row.LastChangedAt = now;
        return true;
    }

    /// <summary>最后一个连接断开。</summary>
    /// <returns>旁观者视角是否发生变化（据此决定要不要广播）。</returns>
    public static bool ApplyDisconnected(UserPresence row, DateTime now, bool allowInvisible)
    {
        Check.NotNull(row);

        // ★ 隐身者在这里必须一动不动：他早就显示为离线，此刻改 LastSeenAt 或广播一条
        // "状态变了"，都会告诉所有人他其实刚刚才真的离开。
        if (IsHidden(row.Status, allowInvisible))
            return false;

        row.LastSeenAt = now;
        row.LastChangedAt = now;
        return true;
    }

    /// <summary>手动切换状态意图。</summary>
    /// <param name="row">要落笔的 presence 行。</param>
    /// <param name="status">新的手动意图。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="goingDark">
    /// 本次切换是否让这个人从「看得见」变成「隐身」。判定需要连接状态，由调用方经
    /// <see cref="IPresenceService.ResolveEffectiveAsync"/> 先算好 —— 已经隐身的人再设一次隐身
    /// 必须为 <see langword="false"/>，否则时间戳会被反复盖章、隐身期间一路往前走。
    /// </param>
    public static void ApplyIntent(UserPresence row, UserPresenceStatus status, DateTime now, bool goingDark)
    {
        Check.NotNull(row);

        // 隐身那一刻即是最后被看见的时刻：此后他与"刚刚关掉应用的人"在旁观者眼里逐字一致。
        if (goingDark) row.LastSeenAt = now;

        row.Status = status;
        // 手动切换即视为活动，清除 auto-away 标记。
        row.IsAutoAway = false;
        row.LastActivityAt = now;
        row.LastChangedAt = now;
    }
}
