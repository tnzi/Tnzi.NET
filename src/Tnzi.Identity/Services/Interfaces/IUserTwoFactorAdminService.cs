namespace Tnzi.Identity.Services;

/// <summary>
/// 管理端对<b>另一个账号</b>的二次验证的控制。按用户 id 寻址，把工作交给 <see cref="ITwoFactorService"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么管理端要有这个</b>：能重置密码的人本来就能拿到一个「密码我控制、二次验证我控制不了」的账号，
/// 只要账号持有人联系不上，这个账号就是他的了 —— 面板只是把一条已经存在的可达路径摆到明处。
/// 而它替代的那条路是「谁丢了手机就找人开数据库客户端」，那是更糟的事。
/// </para>
/// <para>
/// <b>管理端做不了的一件事：替人登记身份验证器。</b>登记需要持有人自己的设备（扫码、把数字敲回来），
/// 所以它永远留在用户中心自助。管理端能把一个已有的身份验证器关掉，永远不能替人开一个 ——
/// 这条不对称不是待补的缺口：<b>别人能替你登记的第二因子不是第二因子</b>。
/// </para>
/// <para>
/// 越出当前租户范围的账号一律答 404，与不存在相同。写操作本身不区分「自己」与「别人」：
/// 管理员对自己的账号也走这里，权限码一样要过。
/// </para>
/// </remarks>
public interface IUserTwoFactorAdminService
{
    /// <summary>按方式的状态：哪些开着、哪个是首选、登录会不会挑战。</summary>
    Task<Result<TwoFactorStatusDto>> GetStatusAsync(Guid userId);

    /// <summary>总开关关掉：登录不再挑战，每种已配置的方式<b>保留</b>，<see cref="ResumeAsync"/> 原样恢复。</summary>
    Task<Result> SuspendAsync(Guid userId);

    /// <summary>总开关打开：保存着的方式重新生效。要求至少已配置一种方式。</summary>
    Task<Result> ResumeAsync(Guid userId);

    /// <summary>
    /// 打开一种基于验证码的方式，其地址必须已在账号上验证过。
    /// 身份验证器在这里被拒绝，理由见类型备注。
    /// </summary>
    Task<Result> EnableMethodAsync(Guid userId, TwoFactorType type);

    /// <summary>关掉一种方式，其它不动。关掉身份验证器会重置它的密钥。</summary>
    Task<Result> DisableMethodAsync(Guid userId, TwoFactorType type);

    /// <summary>选择登录时优先提供的方式；它必须已经开着。</summary>
    Task<Result> SetPreferredAsync(Guid userId, TwoFactorType type);

    /// <summary>
    /// 全部清掉，身份验证器密钥也包括。破坏性操作，给「设备丢了」用：持有人从头重新登记。
    /// </summary>
    Task<Result> ResetAsync(Guid userId);
}
