namespace Tnzi.Storage.Services;

/// <summary>
/// 文件分享服务接口。
/// </summary>
/// <remarks>
/// ★ <b>契约留在父模块、实现在 <c>Tnzi.Storage.Workspace</c></b>：分享端点长在
/// <c>DefaultStorageController</c>（<c>[Route("files")]</c>）与 <c>DefaultStorageAdminController</c>
/// （<c>[Route("admin/files")]</c>）上，这两个控制器都留在父模块，子模块不得在同一路由模板上
/// 另起一个 <c>[DefaultController]</c>。所以父模块<b>可选注入</b>本接口，没有实现时那批端点
/// 回 501 并指名要加载的包 —— URL 一个字不变，少的是能力。
///
/// ★ 因此签名里<b>不出现 <c>FileShare</c> 实体</b>（它随表搬进了子模块）：一律走 DTO。
/// 这也顺带修掉了「控制器边界才投影」这条约定的一个漏口 —— 实体从此没有机会漏进 API 契约。
/// </remarks>
public interface IFileShareService
{
    /// <summary>
    /// 创建分享链接
    /// </summary>
    Task<Result<FileSharePublicDto>> CreateShareAsync(Guid fileId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享信息（管理视角，含令牌与计数；<b>绝不含 PasswordHash</b>）。
    /// </summary>
    /// <remarks>
    /// 只给<b>管理者</b>看：分享的创建者，或对该文件有变更权的人（与 <see cref="CreateShareAsync"/> 要求的同一份权利）；
    /// 另外放行本次请求已凭这条链接通过 <see cref="ValidateShareAccessAsync"/> 的调用（下载流程要凭它拿 FileId）。
    /// 其余一律 404，与令牌不存在无法区分 —— 已撤销 / 已过期的链接也不例外，管理者看到的是它的真实状态。
    /// 收件人自己看的是 <see cref="GetSharePreviewAsync"/>。
    /// </remarks>
    Task<Result<FileSharePublicDto>> GetShareAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销分享。要求与创建同一份权利（创建者，或对文件有变更权）；否则 404。
    /// </summary>
    Task<Result> RevokeShareAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分享链接收件人在下载**之前**看到的信息（文件名 / 大小 / 是否要口令）。
    ///
    /// 只在链接确实可用时返回；已撤销 / 已过期 / 次数用尽一律 404，与"令牌根本不存在"
    /// 无法区分 —— 区分开就等于告诉试探者哪些令牌是真的。
    /// **不做口令校验**：收件人需要先知道"这里要口令",口令只把住取字节那一关。
    /// </summary>
    Task<Result<FileSharePreviewDto>> GetSharePreviewAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证分享访问权限。通过时把该文件记进请求作用域的
    /// <see cref="IFileAccessGrantContext"/> —— 分享链接的凭据是**令牌本身**而不是调用者
    /// 的身份，后续取记录 / 取流因此不再要求调用者本人有权。
    /// </summary>
    Task<Result<bool>> ValidateShareAccessAsync(string shareToken, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子占用一次访问配额：在单条 SQL 中检查"启用 + 未超过 MaxAccessCount"并自增 AccessCount，
    /// 消除"读取计数判超限 → 再自增"两步之间的竞态。
    /// Atomically consumes one access slot: a single SQL statement checks "enabled + below MaxAccessCount"
    /// and increments AccessCount, eliminating the TOCTOU race between read-check and increment.
    /// </summary>
    /// <returns>true = 成功占用一次配额；false = 已超限 / 已禁用 / 不存在。</returns>
    Task<Result<bool>> IncrementShareAccessCountAsync(string shareToken, CancellationToken cancellationToken = default);

    // Admin management methods
    /// <summary>
    /// Get all shares for a specific file
    /// </summary>
    Task<Result<IEnumerable<FileShareSummaryDto>>> GetSharesByFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query active shares with paging and filtering
    /// </summary>
    Task<Result<IPagedList<FileShareSummaryDto>>> GetActiveSharesAsync(ActiveSharesQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch revoke multiple shares
    /// </summary>
    Task<Result<int>> BatchRevokeSharesAsync(IEnumerable<Guid> shareIds, CancellationToken cancellationToken = default);
}
