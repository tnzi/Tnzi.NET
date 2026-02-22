

namespace Tnzi.Identity.Dtos;

/// <summary>
/// 用户登录记录DTO
/// </summary>
public class UserLoginDto
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 登录提供者（如：Google, Microsoft, GitHub等）
    /// </summary>
    public string LoginProvider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 提供者密钥
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 显示名称
    /// </summary>
    public string? ProviderDisplayName { get; set; }
}

