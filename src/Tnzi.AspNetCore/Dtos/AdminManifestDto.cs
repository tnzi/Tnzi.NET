
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Admin manifest - the subset of module diagnostics relevant for an admin
/// frontend that wants to auto-render menus / CRUD pages based on which
/// modules and admin controllers are actually loaded by the host.
///
/// Returned by <c>GET admin/diagnostics/admin-manifest</c>.
/// </summary>
public class AdminManifestDto
{
    /// <summary>
    /// Modules with at least one admin controller, ordered by load order.
    /// </summary>
    public List<AdminModuleEntryDto> Modules { get; set; } = [];
}

/// <summary>
/// One module's admin surface (controllers under <c>admin/*</c> routes).
/// </summary>
public class AdminModuleEntryDto
{
    /// <summary>
    /// Short module name, e.g. <c>"Identity"</c> (extracted from <c>"Tnzi.Identity"</c>).
    /// Frontend uses this as the i18n key suffix (<c>tnzi.admin.modules.identity.label</c>).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fully-qualified module type name, e.g. <c>"Tnzi.Identity"</c>.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly name, e.g. <c>"Tnzi.Identity"</c>.
    /// </summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>
    /// Whether the module is enabled (could be false if disabled via config).
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Admin entities exposed by this module. One entry per admin controller route prefix.
    /// </summary>
    public List<AdminEntityEntryDto> Entities { get; set; } = [];
}

/// <summary>
/// One admin entity surface - a single route prefix under <c>admin/*</c> backed
/// by a controller, with the set of HTTP methods it exposes.
/// </summary>
public class AdminEntityEntryDto
{
    /// <summary>
    /// Entity name extracted from the route, e.g. <c>"users"</c> for <c>admin/users</c>.
    /// Frontend uses this as the page key suffix.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full route template, e.g. <c>"admin/users"</c>.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Distinct HTTP methods exposed for this route, e.g. <c>["GET", "POST", "PUT", "DELETE"]</c>.
    /// </summary>
    public List<string> Methods { get; set; } = [];

    /// <summary>
    /// True when all four basic CRUD verbs are present (GET, POST, PUT, DELETE).
    /// Useful for deciding whether to surface the page as fully editable vs read-only.
    /// </summary>
    public bool HasFullCrud { get; set; }

    /// <summary>
    /// True when the underlying controller carries <c>[DefaultController]</c>
    /// (consumer can override by registering a controller at the same route).
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Controller type name (FQ), e.g. <c>"Tnzi.Identity.Controllers.Admin.DefaultUserAdminController"</c>.
    /// Mostly for diagnostics.
    /// </summary>
    public string ControllerType { get; set; } = string.Empty;
}
