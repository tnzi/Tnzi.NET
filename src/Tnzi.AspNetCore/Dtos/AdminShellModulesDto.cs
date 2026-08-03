
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Admin shell runtime module manifest - the set of framework business modules
/// the host has actually loaded, shaped for the admin frontend to gate which
/// module menus / routes are reachable (so a module the backend never loaded
/// doesn't surface a dead menu that 404s on click).
///
/// Returned by <c>GET admin/shell/modules</c>. Unlike the richer diagnostics
/// <c>admin/diagnostics/admin-manifest</c> (gated on <c>system.diagnostics.view</c>),
/// this payload carries no technical detail and is readable by ANY signed-in
/// admin user - module-availability gating must hold for super-admins and
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

    /// <summary>
    /// Which SignalR hubs the host actually mapped. Separate from
    /// <see cref="Modules"/> because realtime is NOT derivable from it: hubs are
    /// mapped by their owning business module only when <c>SignalRModule</c> (a
    /// framework module, deliberately absent from <see cref="Modules"/>) is also
    /// loaded. Without this the frontend saw "System is loaded" and opened a
    /// connection to a hub that was never mapped.
    /// </summary>
    public AdminShellRealtimeDto Realtime { get; set; } = new();
}

/// <summary>
/// Realtime (SignalR) capability of the host, as seen by the admin frontend.
/// </summary>
public class AdminShellRealtimeDto
{
    /// <summary>
    /// Whether ANY hub is mapped - i.e. whether this host does realtime at all.
    /// False on a host that never loaded <c>SignalRModule</c>; clients must not
    /// open a connection in that case.
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// The mapped hubs, by logical name and request-ready path. Paths already
    /// include the application's <c>PathBase</c>, so a client under an IIS
    /// sub-application gets the sub-path prefix without configuring it by hand.
    /// </summary>
    public List<AdminShellHubDto> Hubs { get; set; } = [];
}

/// <summary>
/// One mapped realtime hub.
/// </summary>
public class AdminShellHubDto
{
    /// <summary>Logical hub name, e.g. <c>"settings"</c> / <c>"chat"</c> / <c>"presence"</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to connect to, <c>PathBase</c> included, e.g. <c>"/api/hubs/settings"</c>.</summary>
    public string Path { get; set; } = string.Empty;
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
    /// config - the frontend hides its menu in that case too.
    /// </summary>
    public bool IsEnabled { get; set; }
}
