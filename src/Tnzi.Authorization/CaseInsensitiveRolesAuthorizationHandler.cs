using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Tnzi.Security.Claims;

namespace Tnzi.Authorization;

/// <summary>
/// 让 <c>[Authorize(Roles="...")]</c> 的角色匹配与框架其余角色判断一样 <b>大小写不敏感</b>。
/// BCL 内置 RolesAuthorizationHandler 用 <see cref="System.Security.Claims.ClaimsPrincipal.IsInRole"/>
/// （角色值大小写敏感）；本 handler 作为补充 —— 同一 requirement 只要任一 handler
/// <c>Succeed</c> 即通过，故本 handler <b>只放宽不收紧</b>（绝不调用 Fail）。
/// </summary>
public sealed class CaseInsensitiveRolesAuthorizationHandler
    : AuthorizationHandler<RolesAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        if (context.User is { } user
            && requirement.AllowedRoles.Any(role => user.IsInRoleIgnoreCase(role)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
