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

    /// <summary>
    /// 取回正在签（或已签成）的那份 PDF。默认内联（给签署页的预览框），
    /// <c>?download=true</c> 时按附件下载。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 收件人凭令牌而不是凭 <c>files/{id}/download</c> 取字节：那条路对匿名一律 404，
    /// 而令牌才是这个人的全部身份。判定仍在服务层（令牌校验写请求级授予），
    /// 本方法只负责把结果按文件送出去。
    /// </para>
    /// <para>
    /// ★ <b>只有可展示的类型才内联</b>（<see cref="FileTypeHelper.IsInlineRenderable"/>），其余一律带文件名
    /// ⇒ <c>Content-Disposition: attachment</c> + <c>Content-Security-Policy: sandbox</c>，与
    /// <c>files/{id}/preview</c> 同形。声明的 Content-Type 来自上传者给的文件名（<c>.html → text/html</c>），
    /// 而本端点匿名可达：不加这道闸，一个模板管理员传一个 <c>payload.html</c> 当渲染稿，收件人打开签署链接，
    /// 脚本就跑在 API 的源上。服务层已只放行 PDF，这里是响应形态上的纵深；消费方整体替换本控制器时请沿用。
    /// <c>nosniff</c> 对两个分支都加：声明的类型就是最终类型。
    /// </para>
    /// </remarks>
    [HttpGet("{token}/document")]
    public virtual async Task<IActionResult> GetDocument(
        string token, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var result = await Requests.GetDocumentByTokenAsync(token, cancellationToken);
        if (!result.Succeeded || result.Data is null)
            return new NotFoundResult();

        var document = result.Data;
        Response.Headers.XContentTypeOptions = "nosniff";

        if (!FileTypeHelper.IsInlineRenderable(document.ContentType))
        {
            Response.Headers.ContentSecurityPolicy = "sandbox";
            return File(document.Content, document.ContentType, document.FileName);
        }

        return download
            ? File(document.Content, document.ContentType, document.FileName)
            : File(document.Content, document.ContentType);
    }

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
