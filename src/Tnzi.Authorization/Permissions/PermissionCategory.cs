namespace Tnzi.Authorization.Permissions;

/// <summary>
/// Coarse audience classification for a permission: who is the permission's
/// admin surface *for*. Drives the two-tier admin model:
/// <list type="bullet">
///   <item><see cref="Business"/> — business-facing administration (users,
///     finance, payment, content, AI agents/skills, …). Members of
///     <c>Authorization:BusinessAdminRoles</c> are implicitly granted every
///     enabled Business permission.</item>
///   <item><see cref="Technical"/> — system/operations administration
///     (diagnostics, performance, logs, MCP servers, sandbox, quotas,
///     system parameters, …). Only explicit grants or the
///     <c>Authorization:SuperAdminRoles</c> bypass reach these.</item>
/// </list>
/// </summary>
/// <remarks>
/// <b>Default is <see cref="Business"/></b> (enum value 0): application-defined
/// permission codes that never declare a category stay reachable by business
/// admins, and pre-existing DB rows migrate as Business. Technical is an
/// explicit opt-in marking for surfaces business admins should not see.
/// </remarks>
public enum PermissionCategory
{
    /// <summary>业务向权限：业务管理员可达。</summary>
    Business = 0,

    /// <summary>技术/系统运维向权限：仅显式授权或超级管理员可达。</summary>
    Technical = 1,
}
