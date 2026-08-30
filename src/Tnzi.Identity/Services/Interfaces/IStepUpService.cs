namespace Tnzi.Identity.Services;

/// <summary>
/// 二次确认（step-up）：在已登录会话之上，要求用户对某个动作当场再证明一次身份。
/// </summary>
/// <remarks>
/// <para>
/// <b>与登录路径共用验证器，但不签发任何登录凭据。</b>确认成功只在服务端留下一条
/// 「某人在某时对某个范围完成了确认」的短期记录，令牌、会话、权限一概不变 ——
/// 一次 step-up 不该顺带延长会话，更不该提升权限。
/// </para>
/// <para>
/// <b>范围（scope）是调用方自定义的字符串</b>，例如 <c>"tip.attachment.download"</c>。
/// 它让一次确认只覆盖它该覆盖的动作：为下载附件做的确认，不应当顺便把「删除全部记录」也放行。
/// 粒度由应用决定，框架不预设任何范围名。
/// </para>
/// <para>
/// ★ <b>确认必须是当前登录用户本人。</b>用别人的 passkey 完成断言不算数 ——
/// 那只证明「有另一个人在场」，而这里要证明的是「就是你」。实现方必须比对断言得到的用户
/// 与当前会话用户，不一致一律拒绝。
/// </para>
/// </remarks>
public interface IStepUpService
{
    /// <summary>
    /// 当前用户在指定范围内是否已完成有效的确认。
    /// </summary>
    /// <param name="scope">确认范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// <b>未启用时恒为 <c>true</c></b>：这个能力是加固项，没开就不该让端点全部瘫掉。
    /// <para>
    /// 配置了 <see cref="Options.StepUpOptions.SingleUse"/> 时，本方法会<b>消费掉</b>那次确认。
    /// 因此它不是一个纯查询，不要拿它做「界面上要不要显示锁图标」这类判断。
    /// </para>
    /// </remarks>
    Task<bool> IsSatisfiedAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用 passkey 完成一次确认。
    /// </summary>
    /// <param name="input">断言结果（与登录用的是同一份数据形态）。</param>
    /// <param name="scope">确认范围。</param>
    /// <remarks>抗钓鱼强度与 passkey 登录完全相同，因为走的就是同一个断言校验。</remarks>
    Task<Result<StepUpGrantDto>> VerifyWithPasskeyAsync(PasskeyCompleteDto input, string scope);

    /// <summary>
    /// 给当前用户发一枚<strong>二次确认专用</strong>的验证码。
    /// </summary>
    /// <param name="type">渠道（Email / Sms）。TOTP 由验证器生成，无需也不能发码。</param>
    /// <returns>成功时 <c>Data</c> 为脱敏后的接收地址，供界面提示「已发送到 …」。</returns>
    /// <remarks>
    /// ★ <b>二次确认必须有自己的发码入口。</b>它验的码用途是
    /// <see cref="VerificationCodePurpose.StepUp"/>，与登录 2FA 的码互不通用 ——
    /// 没有这个入口，用短信/邮箱做二次确认就只能去借别的流程发出来的码，
    /// 而那正是「一枚登录码可以确认一笔转账」的由来。
    /// </remarks>
    Task<Result<string?>> SendCodeAsync(TwoFactorType type);

    /// <summary>
    /// 用二次验证码完成一次确认。
    /// </summary>
    /// <param name="code">验证码。</param>
    /// <param name="type">验证码渠道。</param>
    /// <param name="scope">确认范围。</param>
    /// <remarks>
    /// 强度低于 passkey（短信与邮件码都可被中继），提供它是为了没有 passkey 的部署也能用上，
    /// <b>不是为了让用户在两者之间挑一个更省事的</b>。对高风险动作应当只开 passkey 一条路。
    /// </remarks>
    Task<Result<StepUpGrantDto>> VerifyWithCodeAsync(string code, TwoFactorType type, string scope);
}
