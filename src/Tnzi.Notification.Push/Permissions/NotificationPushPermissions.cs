namespace Tnzi.Notification.Push.Permissions;

/// <summary>
/// 推送设备注册表 admin 面的操作级权限码（2 个）。
/// </summary>
/// <remarks>
/// 随模块走：不发推送的宿主不加载本模块，也就永远不会 seed 这两个码，
/// 权限矩阵里不会多出一块用不上的功能面。父组仍是核心声明的 <c>notification</c>，
/// 于是两块在授权界面里合并成一棵树 —— 与 <c>Tnzi.Finance.Banking</c> 复用
/// <c>finance</c> 组同一形状，子模块不重复声明父组。
/// <para>
/// ★ <b>只有 view 与 delete。</b>注册与刷新是<b>用户端</b>动作（客户端拿到令牌自己上报），
/// admin 没有代人注册设备的场景 —— 真给了 create，那个端点写出来的令牌不对应任何一台
/// 真实设备，推送会一直失败而没人知道为什么。update 同理：设备的字段全部由客户端上报，
/// 运维改它只会让表与现实脱节。
/// </para>
/// </remarks>
public class NotificationPushPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions(
            "notification.pushDevice",
            "Push Devices",
            parentName: "notification",
            actions: CrudActions.View | CrudActions.Delete);
    }
}
