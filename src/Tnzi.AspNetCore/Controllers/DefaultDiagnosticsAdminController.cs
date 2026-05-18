
namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Diagnostics admin controller base class
/// Provides exception statistics query and management endpoints
/// </summary>
[Route("admin/diagnostics")]
[DefaultController]
public class DefaultDiagnosticsAdminController : ApiAdminControllerBase
{
    protected readonly IExceptionStatisticsService ExceptionStatisticsService;

    /// <summary>
    /// Initializes the diagnostics admin controller
    /// </summary>
    /// <param name="exceptionStatisticsService">Exception statistics service</param>
    public DefaultDiagnosticsAdminController(IExceptionStatisticsService exceptionStatisticsService)
    {
        ExceptionStatisticsService = Check.NotNull(exceptionStatisticsService);
    }

    /// <summary>
    /// Get exception summary within a time window
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    /// <returns>Exception summary including totals, breakdowns, and top exceptions</returns>
    [HttpGet("exceptions/summary")]
    public virtual ApiResult<ExceptionSummaryDto> GetExceptionSummary([FromQuery] int minutes = 60)
    {
        var result = ExceptionStatisticsService.GetSummary(minutes);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get recent exception entries
    /// </summary>
    /// <param name="count">Number of recent entries to return (default: 20, max: 500)</param>
    /// <returns>List of recent exception entries</returns>
    [HttpGet("exceptions/recent")]
    public virtual ApiResult<List<ExceptionEntryDto>> GetRecentExceptions([FromQuery] int count = 20)
    {
        var result = ExceptionStatisticsService.GetRecentExceptions(count);
        return result.ToApiResult();
    }

    /// <summary>
    /// Clear all exception statistics
    /// </summary>
    /// <returns>Operation result</returns>
    [HttpDelete("exceptions")]
    public virtual ApiResult ClearExceptions()
    {
        var result = ExceptionStatisticsService.Clear();
        return result.ToApiResult();
    }

    /// <summary>
    /// Get all active controllers with metadata
    /// </summary>
    [HttpGet("controllers")]
    public virtual ApiResult<ControllerDiagnosticsResultDto> GetControllers(
        [FromServices] IActionDescriptorCollectionProvider actionProvider)
    {
        var controllerActions = actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .GroupBy(a => a.ControllerTypeInfo.AsType())
            .Select(g =>
            {
                var controllerType = g.Key;
                var isDefault = controllerType.GetCustomAttributes<DefaultControllerAttribute>().Any();
                var route = g.First().AttributeRouteInfo?.Template ?? "";
                var moduleName = controllerType.Assembly.GetName().Name ?? "";

                return new ControllerInfoDto
                {
                    Type = controllerType.FullName ?? controllerType.Name,
                    Route = route,
                    Module = moduleName,
                    IsDefault = isDefault,
                    Methods = g.Select(a =>
                    {
                        var httpMethod = a.ActionConstraints?
                            .OfType<HttpMethodActionConstraint>()
                            .FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET";
                        return $"{httpMethod} {a.ActionName}";
                    }).ToList()
                };
            })
            .ToList();

        return Ok(new ControllerDiagnosticsResultDto
        {
            TotalCount = controllerActions.Count,
            Controllers = controllerActions
        });
    }

    /// <summary>
    /// Get all loaded modules and their manifests
    /// </summary>
    [HttpGet("modules")]
    public virtual ApiResult<List<ModuleDiagnosticsDto>> GetModules([FromServices] ITnziApplication tnziApp)
    {
        var modules = tnziApp.Modules.Select(m => new ModuleDiagnosticsDto
        {
            Type = m.Type.Name,
            Assembly = m.Assembly.GetName().Name ?? m.Assembly.FullName ?? "",
            IsEnabled = m.IsEnabled,
            InitializationState = m.InitializationState.ToString(),
            DependencyCount = m.Dependencies.Count,
            Manifest = new ModuleManifestDto
            {
                ServiceCount = m.Manifest.Services.Count,
                Controllers = m.Manifest.Controllers.ToList(),
                Events = m.Manifest.Events.ToList(),
                BackgroundTasks = m.Manifest.BackgroundTasks.ToList(),
                Options = m.Manifest.Options.ToList()
            }
        }).ToList();

        return Ok(modules);
    }

    /// <summary>
    /// Get the admin manifest — the subset of loaded modules and admin
    /// controllers shaped for a frontend that wants to auto-render menus
    /// and CRUD pages.
    ///
    /// Powers the <c>useAdminModuleManifest()</c> composable in
    /// <c>@tnzi/ui-admin</c> 0.2.4+. The DTO is intentionally flatter than
    /// <see cref="GetModules"/>: one entity per admin route prefix, with
    /// the HTTP methods that prefix exposes — so the frontend can decide
    /// whether to surface a page (only if route is reachable) and whether
    /// to enable Create/Update/Delete UX (based on supported verbs).
    /// </summary>
    [HttpGet("admin-manifest")]
    public virtual ApiResult<AdminManifestDto> GetAdminManifest(
        [FromServices] ITnziApplication tnziApp,
        [FromServices] IActionDescriptorCollectionProvider actionProvider)
    {
        // Index controller types declared by each loaded module assembly.
        var modulesByAssembly = tnziApp.Modules.ToDictionary(
            m => m.Assembly.GetName().Name ?? m.Assembly.FullName ?? string.Empty);

        // Group all controller actions by their controller type, then bucket
        // by assembly so we can ascribe them to the owning module.
        var adminControllerGroups = actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => (a.AttributeRouteInfo?.Template ?? string.Empty)
                .StartsWith("admin/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.ControllerTypeInfo.AsType())
            .ToList();

        var perModule = new Dictionary<string, AdminModuleEntryDto>();

        foreach (var ctrlGroup in adminControllerGroups)
        {
            var controllerType = ctrlGroup.Key;
            var assemblyName = controllerType.Assembly.GetName().Name ?? string.Empty;
            if (string.IsNullOrEmpty(assemblyName)) continue;

            // Each controller binds to one route prefix.
            var firstAction = ctrlGroup.First();
            var route = firstAction.AttributeRouteInfo?.Template ?? string.Empty;
            if (string.IsNullOrEmpty(route)) continue;

            // Entity name = the segment immediately after "admin/".
            var entityName = ExtractEntityName(route);
            if (string.IsNullOrEmpty(entityName)) continue;

            var isDefault = controllerType.GetCustomAttributes<DefaultControllerAttribute>().Any();

            // Aggregate HTTP methods across all actions of this controller.
            var methods = ctrlGroup
                .SelectMany(a => a.ActionConstraints?
                    .OfType<HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods) ?? [])
                .Select(m => m.ToUpperInvariant())
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            var entity = new AdminEntityEntryDto
            {
                Name = entityName,
                Route = route,
                Methods = methods,
                HasFullCrud = methods.Contains("GET")
                              && methods.Contains("POST")
                              && methods.Contains("PUT")
                              && methods.Contains("DELETE"),
                IsDefault = isDefault,
                ControllerType = controllerType.FullName ?? controllerType.Name,
            };

            if (!perModule.TryGetValue(assemblyName, out var moduleEntry))
            {
                modulesByAssembly.TryGetValue(assemblyName, out var module);
                moduleEntry = new AdminModuleEntryDto
                {
                    Name = ExtractModuleShortName(assemblyName),
                    FullName = module?.Type.FullName ?? assemblyName,
                    Assembly = assemblyName,
                    IsEnabled = module?.IsEnabled ?? true,
                    Entities = [],
                };
                perModule[assemblyName] = moduleEntry;
            }

            moduleEntry.Entities.Add(entity);
        }

        // Sort entities alphabetically inside each module so menu order is
        // stable across runs; sort modules by short name for the same reason.
        foreach (var module in perModule.Values)
        {
            module.Entities = module.Entities.OrderBy(e => e.Name).ToList();
        }

        var orderedModules = perModule.Values
            .OrderBy(m => m.Name)
            .ToList();

        return Ok(new AdminManifestDto { Modules = orderedModules });
    }

    /// <summary>
    /// Extract the entity name from a route like <c>admin/users</c> → <c>"users"</c>
    /// or <c>admin/identity/users</c> → <c>"identity/users"</c> (preserve nested
    /// segments because frontend uses the same form for page lookup).
    /// </summary>
    private static string ExtractEntityName(string route)
    {
        const string prefix = "admin/";
        if (route.Length <= prefix.Length) return string.Empty;
        return route.Substring(prefix.Length).TrimEnd('/');
    }

    /// <summary>
    /// Extract a short, frontend-friendly module name from the assembly:
    /// <c>"Tnzi.Identity"</c> → <c>"Identity"</c>,
    /// <c>"Tnzi.AI.Skills"</c> → <c>"AI.Skills"</c>.
    /// </summary>
    private static string ExtractModuleShortName(string assemblyName)
    {
        const string prefix = "Tnzi.";
        return assemblyName.StartsWith(prefix, StringComparison.Ordinal)
            ? assemblyName.Substring(prefix.Length)
            : assemblyName;
    }
}
