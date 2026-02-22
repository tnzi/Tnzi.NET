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
}

