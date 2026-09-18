namespace Tnzi.SignalR.Metadata;

/// <summary>
/// 框架约定的 SignalR 组名。
/// </summary>
/// <remarks>
/// <para>
/// <c>TnziHub</c> 在连接建立时把每条已认证连接加进 <see cref="ForUser"/> 组，
/// 断开时移出；<see cref="MessagePushService{THub}"/> 按用户推送就投递到这个组。
/// 组由 SignalR 自己维护、经 Redis Backplane 跨实例转发，所以按用户推送不依赖任何进程内状态 ——
/// 这正是它与 <see cref="IConnectionManager"/>（纯内存、只对本实例成立）的分界。
/// </para>
/// <para>
/// ★ 组名字符串只允许从这里出：加入侧与投递侧各自手拼一份时，任何一边改了格式，
/// 另一边不会报错，只是从此一条都收不到。
/// </para>
/// </remarks>
public static class HubGroupNames
{
    /// <summary>按用户寻址的组名前缀。</summary>
    public const string UserGroupPrefix = "User_";

    /// <summary>某个用户全部连接所在的组。</summary>
    public static string ForUser(Guid userId) => $"{UserGroupPrefix}{userId}";

    /// <summary>按租户寻址的组名前缀。</summary>
    public const string TenantGroupPrefix = "Tenant_";

    /// <summary>
    /// 某个租户全部已认证连接所在的组。<c>TnziHub</c> 在连接主体带 <c>tenant_id</c> claim 时加入；
    /// 没有 claim 的连接不进任何租户组 —— 按租户推送时它们收不到，这是关闭方向。
    /// 「发给整个租户」的推送投递到这个组而不是 <c>Clients.All</c>：多租户下 All 会把一家租户的事件送到所有租户。
    /// </summary>
    public static string ForTenant(Guid tenantId) => $"{TenantGroupPrefix}{tenantId}";
}
