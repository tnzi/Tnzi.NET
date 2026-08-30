namespace Tnzi.Identity.Services;

/// <summary>
/// Passkey（WebAuthn）注册与断言。
/// </summary>
/// <remarks>
/// <para>
/// 两段式，与 WebAuthn 本身的形状一致：<c>Begin*</c> 产出一份浏览器选项，
/// 用户完成生物识别后把凭据回传给 <c>Complete*</c>。
/// </para>
/// <para>
/// ★★ <strong>断言成功后走的是与其余五条签发路径同一个出口</strong>
/// （<see cref="IAuthService.IssueTokenAsync"/>：登录守卫 → 2FA 判定 → 会话协调器 → 带 <c>session_id</c> 的令牌）。
/// 运行时另有一个 <c>SignInManager.PasskeySignInAsync</c>，<strong>刻意不用</strong> ——
/// 它走 cookie 登录，会绕过 <c>ILoginGuard</c> 与 <c>ILoginSessionCoordinator</c>，
/// 也就是绕过 IP 白名单这类准入策略和多设备登录策略。
/// </para>
/// <para>
/// <strong>密码学不在框架里。</strong>证明与断言的校验由 ASP.NET Core Identity 的
/// <c>IPasskeyHandler&lt;TUser&gt;</c> 完成，凭据存取由 <c>IUserPasskeyStore&lt;TUser&gt;</c>
/// （EF 侧已实现）完成。本服务只负责接线、挑战状态的存活期，以及把结果接回框架的登录语义。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "passkey 接线刚落地，凭据管理端点与邀请注册的形态可能调整")]
public interface IPasskeyService
{
    /// <summary>
    /// 生成注册选项。
    /// </summary>
    /// <param name="enrollmentToken">
    /// 可选的注册令牌（见 <see cref="IPasskeyEnrollmentTokenService"/>）。
    /// 不给则挂到<strong>当前已登录用户</strong>名下；给了则挂到令牌指向的用户名下，此时允许匿名调用。
    /// </param>
    /// <remarks>
    /// ★ <strong>两条路径都必须有一个"你是谁"的凭据</strong>：要么已登录，要么持有一枚一次性令牌。
    /// 给一个既没登录也没令牌的调用方发注册选项，等于让任何人往任意账号上挂凭据。
    /// 判定在服务层而不是控制器特性上 —— 控制器是 <c>[DefaultController]</c>，可被消费方整体替换。
    /// </remarks>
    Task<Result<PasskeyOptionsDto>> BeginRegistrationAsync(string? enrollmentToken = null);

    /// <summary>
    /// 校验证明并把凭据挂到目标用户名下；用了注册令牌的话，成功后令牌即失效。
    /// </summary>
    /// <param name="input">浏览器凭据 + begin 阶段的状态句柄。</param>
    /// <param name="enrollmentToken">与 begin 阶段同一枚令牌；不给则挂到当前已登录用户名下。</param>
    Task<Result<PasskeyCredentialDto>> CompleteRegistrationAsync(PasskeyCompleteDto input, string? enrollmentToken = null);

    /// <summary>
    /// 生成断言选项。匿名可访问。
    /// </summary>
    /// <param name="input">可选用户名；不给则走 discoverable credential。</param>
    /// <remarks>
    /// ★ 用户名查无此人时<strong>不报错也不提示</strong>，照常返回一份选项 ——
    /// 否则这个匿名端点会变成用户名枚举预言机。
    /// </remarks>
    Task<Result<PasskeyOptionsDto>> BeginAssertionAsync(PasskeyAssertionBeginDto input);

    /// <summary>
    /// 校验断言并签发令牌。匿名可访问。
    /// </summary>
    /// <remarks>
    /// 成功后会把更新过的凭据写回（签名计数器等），这是克隆凭据检测的前提。
    /// 随后交给 <see cref="IAuthService.IssueTokenAsync"/> 完成签发。
    /// </remarks>
    Task<Result<TokenResult>> CompleteAssertionAsync(PasskeyCompleteDto input);

    /// <summary>
    /// 只校验断言，返回它所证明的用户 Id，<strong>不签发任何令牌</strong>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 给「已经登录、但这个动作需要当场再证明一次本人」的场景用（见
    /// <see cref="IStepUpService"/>）。与 <see cref="CompleteAssertionAsync"/> 共用同一段
    /// 密码学校验与凭据写回，<strong>分岔只在最后一步</strong>：一个去换令牌，一个只报告结果。
    /// </para>
    /// <para>
    /// ★ <strong>调用方必须自己比对这个 Id 与当前会话用户。</strong>本方法回答的是
    /// 「这次断言证明了谁」，不是「证明的是不是你」—— 拿别人的 passkey 也能得到一个成功结果。
    /// </para>
    /// </remarks>
    Task<Result<Guid>> VerifyAssertionAsync(PasskeyCompleteDto input);

    /// <summary>
    /// 列出当前用户已注册的凭据。
    /// </summary>
    Task<Result<List<PasskeyCredentialDto>>> GetCredentialsAsync();

    /// <summary>
    /// 删除当前用户的一枚凭据。
    /// </summary>
    /// <param name="credentialId">base64url 形式的凭据标识。</param>
    Task<Result> DeleteCredentialAsync(string credentialId);
}
