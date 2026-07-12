namespace Tnzi.Security.Authorization;

/// <summary>
/// Coarse audience classification for a permission: who is the permission's
/// admin surface *for*. Purely informational metadata for assignment UIs -
/// it does NOT drive any implicit grant. Every non-super-admin user resolves
/// through explicit grants only (deny-by-default).
/// <list type="bullet">
///   <item><see cref="Business"/> — business-facing administration (users,
///     finance, payment, content, AI agents/skills, …).</item>
///   <item><see cref="Technical"/> — system/operations administration
///     (diagnostics, performance, logs, MCP servers, sandbox, quotas,
///     system parameters, …). Assignment UIs render a warning badge on
///     these so operators granting roles can spot dangerous surfaces.</item>
/// </list>
/// </summary>
/// <remarks>
/// <b>Default is <see cref="Business"/></b> (enum value 0): application-defined
/// permission codes that never declare a category need no annotation, and
/// pre-existing DB rows migrate as Business. Technical is an explicit opt-in
/// marking for ops/dangerous surfaces.
/// </remarks>
public enum PermissionCategory
{
    /// <summary>业务向权限。</summary>
    Business = 0,

    /// <summary>技术/系统运维向权限：分配界面渲染警示徽标。</summary>
    Technical = 1,
}
