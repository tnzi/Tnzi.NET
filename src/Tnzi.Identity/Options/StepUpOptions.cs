namespace Tnzi.Identity.Options;

/// <summary>
/// 二次确认（step-up）配置。配置路径 <c>Identity:StepUp</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>解决的是「会话有效」与「此刻本人在场」之间的落差。</b>一次登录换来的会话
/// 通常有效数小时到数天，而这段时间里终端可能没锁、可能已经易手。
/// 对少数几个动作（取走原始附件、批量导出、发起付款、改动安全配置）需要的
/// 不是「这个会话属于谁」，而是「此刻按下这个按钮的是不是本人」。
/// </para>
/// <para>
/// ★ 这与二次验证（2FA）是两件事：2FA 在<b>登录时</b>把关，step-up 在<b>操作时</b>把关。
/// 一个账号可以既没开 2FA 也要求 step-up，反之亦然。
/// </para>
/// </remarks>
public class StepUpOptions
{
    /// <summary>
    /// 是否启用二次确认。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 关闭时 <c>[RequireStepUp]</c> 标记的端点<b>照常放行</b>，端点不会因为没配就全部瘫掉。
    /// 这是刻意的：这个能力是加固项，不是运行前提。要让它成为硬性要求，
    /// 开启本项并在上线校验里核对。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 一次确认的有效秒数。默认 300（5 分钟），下限 30，上限 3600。
    /// </summary>
    /// <remarks>
    /// 这是「刚证明过就别再烦我」的窗口。取值太长等于没做（终端离手后仍然有效），
    /// 太短则会让批量作业每点一下弹一次，用户很快就会去找绕过的办法。
    /// </remarks>
    public int LifetimeSeconds { get; set; } = 300;

    /// <summary>
    /// 一次确认是否只能用一次。默认 <c>false</c>（在有效期内可重复使用）。
    /// </summary>
    /// <remarks>
    /// 开启后每个受保护操作各需一次确认。用于单次动作后果极重的场合
    /// （放款、销毁、把材料交给外部）；用在下载附件这类会连点很多次的操作上会很难用。
    /// </remarks>
    public bool SingleUse { get; set; }

    /// <summary>
    /// 未通过确认时的响应状态码。默认 <c>401</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认 401 而不是 403：403 的含义是「你没有这个权限」，前端据此通常会把入口藏起来；
    /// 而这里的实际情况是「权限有，只是需要再证明一次」，前端应当拉起确认交互后重试。
    /// 响应体里带 <c>STEP_UP_REQUIRED</c> 错误码供前端识别。
    /// </para>
    /// <para>
    /// 若应用的前端把 401 一律当作「会话过期」并跳登录页，把它改成 403 或自定义码，
    /// 并按错误码分支处理。
    /// </para>
    /// </remarks>
    public int ChallengeStatusCode { get; set; } = 401;
}
