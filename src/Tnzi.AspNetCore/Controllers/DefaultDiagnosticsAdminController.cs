
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
}
