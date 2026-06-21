namespace Tnzi.Security.Claims;

[StableApi(Since = "0.1.0")]
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? Id { get; }
    string? UserName { get; }
    Guid? TenantId { get; }
    string[] Roles { get; }

    /// <summary>
    /// 用户邮箱
    /// </summary>
    string? Email => null;

    /// <summary>
    /// 用户手机号
    /// </summary>
    string? PhoneNumber => null;

    bool IsInRole(string roleName);

    /// <summary>
    /// 查找指定类型的首个 claim 值；不存在时返回 null。
    /// 默认实现返回 null；承载 claim 的实现（如 HttpContextCurrentUser）覆盖此方法。
    /// </summary>
    string? FindClaim(string claimType) => null;

    /// <summary>
    /// 查找指定类型的全部 claim 值；不存在时返回空数组。
    /// 用于读取应用自定义的多值 claim（如空格分隔角色、scope 等）。
    /// </summary>
    string[] FindClaims(string claimType) => Array.Empty<string>();
}

