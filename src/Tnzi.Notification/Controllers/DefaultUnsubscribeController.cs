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
