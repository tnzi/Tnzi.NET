using Microsoft.AspNetCore.Authorization;

namespace Tnzi.Signing.Controllers;

/// <summary>
/// 收件人签署端点（匿名）。
/// </summary>
/// <remarks>
/// <para>
/// <b>必须匿名。</b>签署方通常根本不是本系统的用户 —— 客户、对方当事人、见证人都不会有账号，
/// 要求先注册再签字既不现实也没必要。身份由令牌自身担保：令牌是 256 位随机数，
/// 库里只有它的哈希。
/// </para>
/// <para>
/// ★ 三个动作都<b>只收令牌</b>，绝不接受收件人 id 之类的参数 —— 那等于把"我是谁"交给
/// 调用方决定，而这是一条任何人都能访问的公开路径。
/// </para>
/// </remarks>
[DefaultController]
[AllowAnonymous]
[Route("signing")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultSigningController : ApiControllerBase
{
    protected readonly IEnvelopeService Requests;

    public DefaultSigningController(IEnvelopeService requests)
    {
        Requests = Check.NotNull(requests);
    }

    /// <summary>取件：看看要签什么、轮到自己没有。</summary>
    [HttpGet("{token}")]
    public virtual async Task<ApiResult<SigningPacketDto>> Get(string token, CancellationToken cancellationToken)
        => (await Requests.GetByTokenAsync(token, cancellationToken)).ToApiResult();

    /// <summary>提交本人负责的字段与签名。</summary>
    [HttpPost("{token}")]
    public virtual async Task<ApiResult<SigningPacketDto>> Submit(
        string token, [FromBody] SubmitSigningDto input, CancellationToken cancellationToken)
        => (await Requests.SubmitAsync(token, input, cancellationToken)).ToApiResult();

    /// <summary>拒签。</summary>
    [HttpPost("{token}/decline")]
    public virtual async Task<ApiResult<SigningPacketDto>> Decline(
        string token, [FromBody] DeclineSigningDto? input, CancellationToken cancellationToken)
        => (await Requests.DeclineAsync(token, input?.Reason, cancellationToken)).ToApiResult();
}

/// <summary>拒签请求。</summary>
public class DeclineSigningDto
{
    /// <summary>拒签原因（可空）</summary>
    public string? Reason { get; set; }
}
