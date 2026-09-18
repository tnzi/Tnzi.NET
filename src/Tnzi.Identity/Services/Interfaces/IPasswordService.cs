
namespace Tnzi.Identity.Services;

/// <summary>
/// 密码服务接口
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// 忘记密码（发送重置链接）。控制器入口：启用 <c>Identity:Captcha:EnableCaptchaOnPasswordRecovery</c> 时先过人机验证，再委托到按邮箱的重载。
    /// </summary>
    Task<Result<string>> ForgotPasswordAsync(ForgotPasswordDto input);

    /// <summary>
    /// 忘记密码（发送重置链接），不含人机验证。供服务间调用；HTTP 入口应走带 DTO 的重载。
    /// </summary>
    Task<Result<string>> ForgotPasswordAsync(string email);

    /// <summary>
    /// 通过Token重置密码（用户忘记密码流程）
    /// </summary>
    Task<Result<string>> ResetPasswordByTokenAsync(string email, string token, string newPassword);

    /// <summary>
    /// 修改密码（用户主动修改，需要当前密码）
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    /// <summary>
    /// 重置密码（管理员重置，不需要当前密码）
    /// </summary>
    /// <param name="userId">目标用户</param>
    /// <param name="newPassword">新密码</param>
    /// <param name="requireChangeOnNextLogin">
    /// 要求本人下次登录时必须修改。默认 <c>true</c> —— 管理员设的密码是经带外通道递过去的，
    /// 那条通道上谁都可能看见。
    /// </param>
    Task<Result> ResetPasswordByAdminAsync(Guid userId, string newPassword, bool requireChangeOnNextLogin = true);

    /// <summary>
    /// 直接把一个已经证明过身份的账号的密码设成新值。<b>只做这一步</b>：
    /// 不校验旧密码、不校验令牌 —— 调用方必须已经证明了「这个人有权改这个账号的密码」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个调用方，证明方式不同但结论相同：
    /// <see cref="IPendingActionService"/>（凭挑战令牌，而令牌是在凭据与 2FA 都过关之后才签发的）
    /// 与 <c>AuthService.ResetPasswordByCodeAsync</c>（凭发到已验证地址上的一次性验证码）。
    /// </para>
    /// <para>
    /// ★★★ <strong>名字刻意不带任何一个调用方的场景。</strong>它原名
    /// <c>SetPasswordForPendingActionAsync</c>，于是找回密码那条路径的实现者
    /// 不会认为它与自己有关，转而手写了一遍前半段 —— 并且漏掉了后半段的会话撤销与密码历史，
    /// 使得「改了密码」之后被盗的会话原样存活。带场景的名字会把共享出口藏起来。
    /// </para>
    /// <para>
    /// 收尾与其它改密路径逐条一致：强度校验 → 历史查重 → 写入 → 记历史 →
    /// <b>撤销该用户全部会话（含当前）</b>。走到这条路的密码要么是别人给的、要么是被找回的，
    /// 两种情况下保留任何既有会话都没有道理。
    /// </para>
    /// </remarks>
    /// <param name="user">目标账号（调用方已完成身份证明）。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="revokeExistingSessions">
    /// 是否撤销该账号既有的全部会话与刷新令牌，默认 <c>true</c>。
    /// <para>
    /// ★ 只有一种调用方该传 <c>false</c>：账号<b>此前没有任何密码</b>（快速注册的账号
    /// 首次设密码）。那时不存在「一个可能被别人知道的旧凭据」要作废，而唯一在线的会话
    /// 正是本人刚刚凭验证码换来的那一条 —— 撤掉它等于用户设完密码当场被登出。
    /// 凡是在<b>替换</b>一个已有密码，一律用默认值。
    /// </para>
    /// </param>
    Task<Result> ForceSetPasswordAsync(User user, string newPassword, bool revokeExistingSessions = true);
}
