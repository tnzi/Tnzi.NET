using System.Security.Claims;

namespace Tnzi.Security.Claims;

/// <summary>
/// ClaimsPrincipal 角色判断扩展。
/// 框架约定：角色比较一律 <b>大小写不敏感</b>（与 <c>ICurrentUser.IsInRole</c> /
/// SuperAdmin 判定 / ASP.NET Identity 的 NormalizedName 唯一约束保持一致）。
/// 而 BCL <see cref="ClaimsPrincipal.IsInRole(string)"/> 对角色<b>值</b>是大小写敏感
/// (Ordinal) 的，会造成同一身份在 <c>[Authorize(Roles=)]</c> 与框架其余校验点结果不一致。
/// 框架内所有「按角色名」判断改用本扩展，统一为大小写不敏感。
/// </summary>
public static class ClaimsPrincipalRoleExtensions
{
    /// <summary>
    /// 判断 principal 是否具有指定角色（大小写不敏感，OrdinalIgnoreCase）。
    /// 按每个 identity 的 <see cref="ClaimsIdentity.RoleClaimType"/> 取角色 claim 比对。
    /// </summary>
    public static bool IsInRoleIgnoreCase(this ClaimsPrincipal principal, string role)
    {
        Check.NotNull(principal);
        if (string.IsNullOrEmpty(role))
        {
            return false;
        }

        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.FindAll(identity.RoleClaimType))
            {
                if (string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
