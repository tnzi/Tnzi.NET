using Microsoft.AspNetCore.Authorization;

namespace Tnzi.Notification.Controllers;

/// <summary>
/// 一键退订端点（匿名）。
/// </summary>
/// <remarks>
/// <para>
/// <b>必须匿名。</b>收件人未必是本系统的注册用户（客户名单、导入的联系人、已注销的账号），
/// 而且要求先登录才能退订，本身就违背"一键退订"的合规要求。身份由令牌自身的签名担保。
/// </para>
/// <para>
/// <b>三个动作按 HTTP 语义分开：</b><c>GET preview</c> 只回显将要退订的地址（供落地页确认，
/// 不产生副作用）；<c>POST</c> 才真正退订；<c>POST resubscribe</c> 是给"点错了"的人的回头路。
/// 把退订放在 GET 上会被邮件客户端的链接预取器<b>替收件人点掉</b> —— 这是真实发生过的事故，
/// 也是 RFC 8058 的一键退订走 POST 的原因。
/// </para>
/// </remarks>
[DefaultController]
[AllowAnonymous]
[Route("notifications/unsubscribe")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultUnsubscribeController : ApiControllerBase
{
    protected readonly INotificationOptOutService OptOut;

    public DefaultUnsubscribeController(INotificationOptOutService optOut)
    {
        OptOut = Check.NotNull(optOut);
    }

    /// <summary>
    /// 回显令牌指向的退订对象，供落地页在真正退订前确认。无副作用。
    /// </summary>
    [HttpGet]
    public virtual ApiResult<UnsubscribePreviewDto> Preview([FromQuery] string token)
    {
        var payload = OptOut.ResolveUnsubscribeToken(token);
        if (payload == null)
            return ApiResult<UnsubscribePreviewDto>.Error("This unsubscribe link is not valid.", 400);

        return ApiResult<UnsubscribePreviewDto>.Ok(new UnsubscribePreviewDto
        {
            // 掩码后回显：退订链接可能被转发，完整地址不该对拿到链接的任何人可见。
            MaskedAddress = MaskAddress(payload.Address),
            Channel = payload.Channel,
            Category = payload.Category,
        });
    }

    /// <summary>
    /// 执行退订。
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult> Unsubscribe([FromBody] UnsubscribeRequestDto input, CancellationToken cancellationToken)
    {
        Check.NotNull(input);
        var payload = OptOut.ResolveUnsubscribeToken(input.Token);
        if (payload == null)
            return ApiResult.Error("This unsubscribe link is not valid.", 400);

        var result = await OptOut.OptOutAsync(
            payload.Address, payload.Channel, payload.Category,
            source: "one-click link", reason: input.Reason, cancellationToken: cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// RFC 8058 一键退订：邮件服务商<b>直接</b> POST 到这里，收件人不会打开浏览器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与上面那个 <c>POST</c> 的差别只在<b>谁来调</b>：那个是落地页在用户确认后调的，令牌在
    /// JSON 体里；这个是 Gmail / Outlook / Apple Mail 那颗「退订」按钮触发的，令牌在**查询串**里
    /// （信头里那个 URI 是什么样，服务商就 POST 到什么地址），请求体是固定的
    /// <c>List-Unsubscribe=One-Click</c> 表单，本方法不读它。
    /// </para>
    /// <para>
    /// ★ <b>必须与落地页分成两个端点</b>：一键退订**没有确认步骤**（那正是「一键」的含义），
    /// 而落地页那条必须先让人看清自己要退订什么。把它们合成一个，等于要么给一键流程加一道
    /// 服务商不会走的确认，要么把落地页的确认变成摆设。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// ★ <b>刻意不加 <c>[Consumes]</c></b>：本方法一个字节的请求体都不读，令牌在查询串里。
    /// 加上它只会在服务商发来一个我们没列举的 <c>Content-Type</c>（或干脆不带）时回 415 ——
    /// 而那意味着收件人按了「退订」而什么也没发生，正是这条链路要消灭的失败形态。
    /// 能拒绝的东西越少，这个端点越可靠。
    /// </remarks>
    [HttpPost("one-click")]
    public virtual async Task<ApiResult> OneClick([FromQuery] string token, CancellationToken cancellationToken)
    {
        var payload = OptOut.ResolveUnsubscribeToken(token);
        if (payload == null)
            return ApiResult.Error("This unsubscribe link is not valid.", 400);

        var result = await OptOut.OptOutAsync(
            payload.Address, payload.Channel, payload.Category,
            source: "one-click header", reason: null, cancellationToken: cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销退订，给点错了的人一条回头路。
    /// </summary>
    [HttpPost("resubscribe")]
    public virtual async Task<ApiResult> Resubscribe([FromBody] UnsubscribeRequestDto input, CancellationToken cancellationToken)
    {
        Check.NotNull(input);
        var payload = OptOut.ResolveUnsubscribeToken(input.Token);
        if (payload == null)
            return ApiResult.Error("This unsubscribe link is not valid.", 400);

        var result = await OptOut.OptInAsync(
            payload.Address, payload.Channel, payload.Category, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>`a***@example.com` / `•••••2671`，够收件人确认是自己，又不对转发者泄露全址。</summary>
    private static string MaskAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return string.Empty;

        var at = address.IndexOf('@');
        if (at > 0)
            return $"{address[0]}***{address[at..]}";

        return address.Length <= 4 ? new string('*', address.Length) : $"•••••{address[^4..]}";
    }
}
