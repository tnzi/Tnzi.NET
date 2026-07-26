
namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Admin shell controller - serves runtime bootstrap information the admin
/// frontend needs to render correctly, independent of the permission system.
///
/// Deliberately declares NO class-level permission code (bare
/// <see cref="ApiAdminControllerBase"/> authentication only), because its
/// signal - which framework modules the host loaded - must be readable by
/// EVERY signed-in admin user, super-admins and permission-exempt paths
/// included: module-availability menu gating has to hold for them too, and a
/// technical permission gate (as on the diagnostics manifest) would 403 a
/// plain business admin and make the gating inconsistent. The payload carries
/// no sensitive data (module short names + enabled flags only).
/// </summary>
[Route("admin/shell")]
[DefaultController]
public class DefaultAdminShellController : ApiAdminControllerBase
{
    /// <summary>
    /// List the framework business modules (each a <c>TnziApplicationModule</c>)
    /// the host has loaded, by short name + enabled flag. The admin frontend
    /// gates top-level module menus / routes off this: a module absent here
    /// (never loaded) has its menu hidden and its pages made unreachable, so it
    /// never surfaces a dead link - and because this is orthogonal to
    /// permissions, the gating holds for super-admins too. When the endpoint is
    /// unavailable (older backend / network failure) the frontend falls back to
    /// showing everything, so this is purely additive.
    /// </summary>
    [HttpGet("modules")]
    public virtual ApiResult<AdminShellModulesDto> GetModules([FromServices] ITnziApplication tnziApp)
    {
        var modules = tnziApp.Modules
            .Where(m => m.Instance is TnziApplicationModule)
            .Select(m => new
            {
                Name = ExtractModuleShortName(m.Assembly.GetName().Name ?? string.Empty),
                m.IsEnabled,
            })
            .Where(x => !string.IsNullOrEmpty(x.Name))
            // One assembly = one business module in practice, but fold defensively
            // so a duplicate short name can't emit two rows; enabled if any wins.
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AdminShellModuleDto
            {
                Name = g.Key,
                IsEnabled = g.Any(x => x.IsEnabled),
            })
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        return Ok(new AdminShellModulesDto { Modules = modules });
    }

    /// <summary>
    /// Extract a short module name from the assembly:
    /// <c>"Tnzi.Identity"</c> → <c>"Identity"</c>, <c>"Tnzi.AI.Skills"</c> → <c>"AI.Skills"</c>.
    /// </summary>
    private static string ExtractModuleShortName(string assemblyName)
    {
        const string prefix = "Tnzi.";
        return assemblyName.StartsWith(prefix, StringComparison.Ordinal)
            ? assemblyName.Substring(prefix.Length)
            : assemblyName;
    }
}
