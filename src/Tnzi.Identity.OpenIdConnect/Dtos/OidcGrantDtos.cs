using System.ComponentModel.DataAnnotations;

namespace Tnzi.Identity.OpenIdConnect.Dtos;

/// <summary>一条授权：某个用户对某个客户端的同意（或登录时自动产生的授权）。</summary>
public class OidcGrantDto
{
    /// <summary>授权 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>用户 Id。</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户的显示名（昵称 → 全名 → 用户名）。只有管理端列表填它：「谁授权了这个应用」读的是人，不是 Id。
    /// 账号已删除或解析不到时为空，Id 仍在 <see cref="UserId"/>。
    /// </summary>
    public string? UserDisplayName { get; set; }

    /// <summary>用户的账号邮箱。只有管理端列表填它；没有邮箱或解析不到时为空。</summary>
    public string? UserEmail { get; set; }

    /// <summary>客户端 client_id。</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>客户端显示名。</summary>
    public string? ClientDisplayName { get; set; }

    /// <summary>授予的作用域。</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>状态（<c>valid</c> / <c>revoked</c>）。</summary>
    public string? Status { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTime? CreationDate { get; set; }
}

/// <summary>授权分页查询（管理端）。</summary>
public class OidcGrantQueryDto : PagedQueryDto
{
    /// <summary>按用户过滤。</summary>
    public Guid? UserId { get; set; }

    /// <summary>按 client_id 过滤。</summary>
    public string? ClientId { get; set; }

    /// <summary>是否包含已撤销的授权。默认只看有效的。</summary>
    public bool IncludeRevoked { get; set; }
}

/// <summary>同意页需要的展示信息。</summary>
public class OidcConsentInfoDto
{
    /// <summary>client_id。</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>客户端显示名。</summary>
    public string? DisplayName { get; set; }

    /// <summary>说明。</summary>
    public string? Description { get; set; }

    /// <summary>本次申请的作用域（已按客户端允许的范围裁剪）。</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>用户是否已经同意过（已同意时同意页可以直接跳回）。</summary>
    public bool AlreadyGranted { get; set; }
}

/// <summary>用户在同意页点了「同意」。</summary>
public class GrantOidcConsentDto
{
    /// <summary>client_id。</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>同意的作用域（以空格分隔或逐项列出均可，<c>openid</c> 可省略）。</summary>
    public List<string>? Scopes { get; set; }
}
