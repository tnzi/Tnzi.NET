
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Admin shell runtime module manifest — the set of framework business modules
/// the host has actually loaded, shaped for the admin frontend to gate which
/// module menus / routes are reachable (so a module the backend never loaded
/// doesn't surface a dead menu that 404s on click).
///
/// Returned by <c>GET admin/shell/modules</c>. Unlike the richer diagnostics
/// <c>admin/diagnostics/admin-manifest</c> (gated on <c>system.diagnostics.view</c>),
/// this payload carries no technical detail and is readable by ANY signed-in
/// admin user — module-availability gating must hold for super-admins and
/// permission-exempt paths too, so the signal cannot sit behind a permission
/// code that a plain business admin lacks.
/// </summary>
public class AdminShellModulesDto
{
    /// <summary>
    /// Loaded framework business modules (each a <c>TnziApplicationModule</c>),
    /// by short name. The frontend matches these against its top-level module
    /// route names (case-insensitive, dot/dash normalized).
    /// </summary>
    public List<AdminShellModuleDto> Modules { get; set; } = [];
}

/// <summary>
/// One loaded framework business module, identified by its short name.
/// </summary>
public class AdminShellModuleDto
{
    /// <summary>
    /// Short module name, e.g. <c>"Identity"</c> (extracted from the module
    /// assembly <c>"Tnzi.Identity"</c>). Matches the frontend's top-level
    /// module route name (<c>identity</c>) after case-insensitive, dot/dash
    /// normalization.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the module is enabled. A module can be loaded yet disabled via
    /// config — the frontend hides its menu in that case too.
    /// </summary>
    public bool IsEnabled { get; set; }
}
