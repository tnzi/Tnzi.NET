namespace Tnzi.Documents.Signing.Controllers;

/// <summary>
/// 签署请求管理端点：发起 / 发出 / 查看 / 作废。
/// </summary>
/// <remarks>
/// ★ 与收件人面（<see cref="DefaultSigningController"/>，匿名 + 一次性令牌）是**两条完全
/// 分开的路径**，共用同一个服务但不共用任何入口。合并会让"谁能看这份文件"取决于参数
/// 而不是路由 —— 而其中一条路由是任何人都能访问的。
/// </remarks>
[DefaultController]
[Route("admin/signing/requests")]
[ApiAuthorize(PermissionName = "signing.request.view")]
public class DefaultSigningRequestAdminController : ApiAdminControllerBase
{
    protected readonly IEnvelopeService Requests;

    public DefaultSigningRequestAdminController(IEnvelopeService requests)
    {
        Requests = Check.NotNull(requests);
    }

    /// <summary>请求详情（含收件人进度、成品与完成证书）。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EnvelopeDto>> Get(Guid id, CancellationToken cancellationToken)
        => (await Requests.GetAsync(id, cancellationToken)).ToApiResult();

    /// <summary>按模板发起一份请求（草稿，尚未发出）。</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "signing.request.create")]
    public virtual async Task<ApiResult<EnvelopeDto>> Create(
        [FromBody] CreateEnvelopeDto input, CancellationToken cancellationToken)
        => (await Requests.CreateAsync(input, cancellationToken)).ToApiResult();

    /// <summary>
    /// 发出：签发一次性链接。
    /// </summary>
    /// <remarks>
    /// ★ 明文令牌**只在这一次响应里出现**（库里存的是哈希）。调用方拿到就要发出去 ——
    /// 丢了只能重发，重发会作废旧链接。
    /// </remarks>
    [HttpPost("{id:guid}/send")]
    [ApiAuthorize(PermissionName = "signing.request.update")]
    public virtual async Task<ApiResult<IReadOnlyList<IssuedSigningLink>>> Send(
        Guid id, CancellationToken cancellationToken)
        => (await Requests.SendAsync(id, cancellationToken)).ToApiResult();

    /// <summary>作废（已完成的请求不可作废——那份文件已经签成了）。</summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "signing.request.update")]
    public virtual async Task<ApiResult> Void(Guid id, CancellationToken cancellationToken)
        => (await Requests.VoidAsync(id, cancellationToken)).ToApiResult();
}
