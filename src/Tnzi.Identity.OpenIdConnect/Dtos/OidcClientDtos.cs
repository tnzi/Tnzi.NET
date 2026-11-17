using System.ComponentModel.DataAnnotations;

namespace Tnzi.Identity.OpenIdConnect.Dtos;

/// <summary>客户端（依赖方应用）。</summary>
public class OidcClientDto
{
    /// <summary>记录 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>协议里的 client_id。</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>显示名（同意页与「已授权应用」列表展示它）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>说明。</summary>
    public string? Description { get; set; }

    /// <summary>客户端类型。</summary>
    public OidcClientType ClientType { get; set; }

    /// <summary>同意方式。</summary>
    public OidcConsentType ConsentType { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>是否要求 PKCE（公开客户端恒为 true）。</summary>
    public bool RequirePkce { get; set; }

    /// <summary>登录回调地址。</summary>
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>登出后回跳地址。</summary>
    public List<string> PostLogoutRedirectUris { get; set; } = [];

    /// <summary>允许申请的作用域（<c>openid</c> 恒允许，不列出）。</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>消费方自己的记录标识。</summary>
    public string? ExternalReference { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>创建客户端的结果：机密客户端的明文密钥只在这里出现一次。</summary>
public class OidcClientSecretDto
{
    /// <summary>客户端。</summary>
    public OidcClientDto Client { get; set; } = new();

    /// <summary>明文客户端密钥（公开客户端为 null）。库里只存哈希，丢了只能轮换。</summary>
    public string? ClientSecret { get; set; }
}

/// <summary>创建客户端。</summary>
public class CreateOidcClientDto
{
    /// <summary>client_id；留空时生成一个随机值。只允许字母、数字、<c>-</c>、<c>_</c>、<c>.</c>。</summary>
    [StringLength(100)]
    [RegularExpression("^[A-Za-z0-9._-]+$")]
    public string? ClientId { get; set; }

    /// <summary>显示名。</summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>说明。</summary>
    [StringLength(OidcClient.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>客户端类型。默认机密客户端。</summary>
    public OidcClientType ClientType { get; set; }

    /// <summary>同意方式。默认 <see cref="OidcConsentType.Explicit"/>：第一方应用要显式选 Implicit。</summary>
    public OidcConsentType ConsentType { get; set; } = OidcConsentType.Explicit;

    /// <summary>是否要求 PKCE。默认 true；公开客户端忽略此值、恒要求。</summary>
    public bool RequirePkce { get; set; } = true;

    /// <summary>登录回调地址（至少一个）。</summary>
    [Required]
    public List<string> RedirectUris { get; set; } = null!;

    /// <summary>登出后回跳地址。</summary>
    public List<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>允许申请的作用域。留空时 = <c>profile</c> + <c>email</c>。</summary>
    public List<string>? Scopes { get; set; }

    /// <summary>消费方自己的记录标识。</summary>
    [StringLength(OidcClient.ExternalReferenceMaxLength)]
    public string? ExternalReference { get; set; }
}

/// <summary>修改客户端（client_id 与客户端类型不可改：改了等于换一个客户端）。</summary>
public class UpdateOidcClientDto
{
    /// <summary>显示名。</summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>说明。</summary>
    [StringLength(OidcClient.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>同意方式。</summary>
    public OidcConsentType ConsentType { get; set; }

    /// <summary>是否要求 PKCE（公开客户端忽略此值）。</summary>
    public bool RequirePkce { get; set; } = true;

    /// <summary>登录回调地址（至少一个）。</summary>
    [Required]
    public List<string> RedirectUris { get; set; } = null!;

    /// <summary>登出后回跳地址。</summary>
    public List<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>允许申请的作用域。</summary>
    public List<string>? Scopes { get; set; }

    /// <summary>消费方自己的记录标识。</summary>
    [StringLength(OidcClient.ExternalReferenceMaxLength)]
    public string? ExternalReference { get; set; }
}

/// <summary>客户端分页查询。</summary>
public class OidcClientQueryDto : PagedQueryDto
{
    /// <summary>按 client_id / 显示名模糊匹配。</summary>
    public string? Keyword { get; set; }

    /// <summary>按外部引用精确匹配。</summary>
    public string? ExternalReference { get; set; }

    /// <summary>按启停状态过滤。</summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>启停客户端。</summary>
public class SetOidcClientEnabledDto
{
    /// <summary>目标状态。</summary>
    public bool Enabled { get; set; }
}
