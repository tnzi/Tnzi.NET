namespace Tnzi.Storage.Dtos;

// 对外分享链接的 API 契约。
//
// ★ 实现（`FileShareService` 与 `Storage_Share` 表）住在可选子模块 `Tnzi.Storage.Workspace`，
//   而这些 DTO 留在父模块：`DefaultStorageController` / `DefaultStorageAdminController` 上那批
//   `files/{id}/share`、`files/share/{token}/*`、`admin/files/shares/*` 端点没有搬走，
//   它们的签名就是这些类型。契约留在声明端点的那个程序集里，子模块反过来引用它 ——
//   依赖方向恒为子 → 父。

/// <summary>
/// 创建分享请求
/// </summary>
public class CreateShareRequest
{
    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 最大访问次数
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// 分享密码
    /// </summary>
    [MaxLength(128)]
    public string? Password { get; set; }
}
/// <summary>
/// File share summary DTO (for admin listing)
/// </summary>
public class FileShareSummaryDto
{
    /// <summary>
    /// Share ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// File ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Original file name
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// Share token
    /// </summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>
    /// Expiration time
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Access count
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// Max access count
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// Whether password is required
    /// </summary>
    public bool RequirePassword { get; set; }

    /// <summary>
    /// Whether the share is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the share is expired
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// Whether the share has reached max access count
    /// </summary>
    public bool IsExhausted => MaxAccessCount.HasValue && AccessCount >= MaxAccessCount.Value;

    /// <summary>
    /// When the link was last used successfully (null = never used).
    /// Answers the question a share list is most often opened to answer.
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Creation time
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Creator ID
    /// </summary>
    public Guid? CreatorId { get; set; }
}
/// <summary>
/// Public-facing file share DTO (never exposes PasswordHash).
/// 对外暴露的分享信息（绝不包含 PasswordHash），用于匿名可达的分享端点。
/// </summary>
public class FileSharePublicDto
{
    /// <summary>
    /// Share ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// File ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Share token
    /// </summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>
    /// Expiration time (null = never expires)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Max access count (null = unlimited)
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// Current access count
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// Whether a password is required to access this share
    /// </summary>
    public bool RequirePassword { get; set; }

    /// <summary>
    /// Whether the share is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Creation time
    /// </summary>
    public DateTime CreationTime { get; set; }
}
/// <summary>
/// Active shares query request
/// </summary>
public class ActiveSharesQueryRequest : PagedQueryDto
{
    /// <summary>
    /// Filter by file ID
    /// </summary>
    public Guid? FileId { get; set; }

    /// <summary>
    /// Filter by creator ID
    /// </summary>
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// Include expired shares (default false)
    /// </summary>
    public bool IncludeExpired { get; set; } = false;

    /// <summary>
    /// Include disabled shares (default false)
    /// </summary>
    public bool IncludeDisabled { get; set; } = false;
}
/// <summary>
/// Body of the anonymous share-password check.
/// </summary>
public class VerifyShareRequest
{
    /// <summary>Password to check. Null / empty for links that need none.</summary>
    public string? Password { get; set; }
}
/// <summary>
/// What a share-link recipient is shown BEFORE downloading: enough to decide
/// whether to trust the link, and nothing more.
/// </summary>
/// <remarks>
/// Deliberately narrower than `FileSharePublicDto`: no `fileId` (an anonymous
/// visitor has no business learning internal ids), no access counts, no creator.
/// Returned only while the link is actually usable - an expired, exhausted or
/// revoked token gets a plain 404, so probing tells the caller nothing.
/// </remarks>
public class FileSharePreviewDto
{
    /// <summary>File name as uploaded, so the recipient can tell what they are about to open.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>MIME type, for picking an icon.</summary>
    public string? ContentType { get; set; }

    /// <summary>True when the link asks for a password before it hands over the file.</summary>
    public bool RequirePassword { get; set; }

    /// <summary>When the link stops working (null = no expiry).</summary>
    public DateTime? ExpiresAt { get; set; }
}
