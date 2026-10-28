
namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Default diagnostics admin controller (activated by HostingModule).
/// Provides exception statistics, controller/module inspection and the admin manifest.
/// </summary>
[Route("admin/diagnostics")]
[DefaultController]
[ApiAuthorize(PermissionName = "system.diagnostics.view")]
public class DefaultDiagnosticsAdminController : ApiAdminControllerBase
{
    /// <summary>
    /// 异常统计服务；<c>AspNetCore:ExceptionHandling:EnableMetrics=false</c> 时不注册，为 null。
    /// </summary>
    /// <remarks>
    /// 刻意做成可选：必填参数缺席时 <c>ConditionalControllerProvider</c> 会把整个控制器移除，
    /// 关掉一个内存里的异常环形缓冲就会连带让敏感端点清单、模块清单、admin 清单全部 404。
    /// 现在只有三个异常统计端点在缺席时答 501，其余照常。
    /// </remarks>
    protected readonly IExceptionStatisticsService? ExceptionStatisticsService;

    private const string ExceptionStatisticsDisabledMessage =
        "Exception statistics are disabled in this deployment. Set AspNetCore:ExceptionHandling:EnableMetrics to true to enable them.";

    /// <summary>
    /// Initializes the diagnostics admin controller
    /// </summary>
    /// <param name="exceptionStatisticsService">Exception statistics service; absent when exception metrics are disabled</param>
    public DefaultDiagnosticsAdminController(IExceptionStatisticsService? exceptionStatisticsService = null)
    {
        ExceptionStatisticsService = exceptionStatisticsService;
    }

    /// <summary>
    /// Get exception summary within a time window
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    /// <returns>Exception summary including totals, breakdowns, and top exceptions; 501 when exception metrics are disabled</returns>
    [HttpGet("exceptions/summary")]
    public virtual ApiResult<ExceptionSummaryDto> GetExceptionSummary([FromQuery] int minutes = 60)
    {
        // 501 而不是 503：统计没开是这个部署的配置，不是暂时性故障
        if (ExceptionStatisticsService == null)
            return ApiResult<ExceptionSummaryDto>.Error(ExceptionStatisticsDisabledMessage, StatusCodes.Status501NotImplemented);

        var result = ExceptionStatisticsService.GetSummary(minutes);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get recent exception entries
    /// </summary>
    /// <param name="count">Number of recent entries to return (default: 20, max: 500)</param>
    /// <returns>List of recent exception entries; 501 when exception metrics are disabled</returns>
    [HttpGet("exceptions/recent")]
    public virtual ApiResult<List<ExceptionEntryDto>> GetRecentExceptions([FromQuery] int count = 20)
    {
        if (ExceptionStatisticsService == null)
            return ApiResult<List<ExceptionEntryDto>>.Error(ExceptionStatisticsDisabledMessage, StatusCodes.Status501NotImplemented);

        var result = ExceptionStatisticsService.GetRecentExceptions(count);
        return result.ToApiResult();
    }

    /// <summary>
    /// Clear all exception statistics
    /// </summary>
    /// <returns>Operation result; 501 when exception metrics are disabled</returns>
    [HttpDelete("exceptions")]
    [ApiAuthorize(PermissionName = "system.diagnostics.execute")]
    public virtual ApiResult ClearExceptions()
    {
        if (ExceptionStatisticsService == null)
            return ApiResult.Error(ExceptionStatisticsDisabledMessage, StatusCodes.Status501NotImplemented);

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
    /// List every sensitive endpoint that is currently active in this process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 回答的是一个具体问题：<strong>这个部署到底开着哪些"知道了会改变安全评审结论"的端点。</strong>
    /// 框架的 <c>[DefaultController]</c> 自动激活，模块自带的端点无需应用做任何事就挂上路由；
    /// 在此之前想知道开了什么，只能逐模块读源码 —— 那是一次性的人工劳动，
    /// 而<strong>下一个版本新增的敏感端点不会有任何机制提醒已经审过的应用重审</strong>。
    /// 标了 <see cref="SensitiveEndpointAttribute"/> 的端点会自动出现在这里。
    /// </para>
    /// <para>
    /// ★ <strong>数据源是 <see cref="IActionDescriptorCollectionProvider"/>，即最终生效的路由表</strong>，
    /// 不是程序集扫描。所以经 <c>ControllerFilter:DisabledEndpoints</c> 抑制掉的端点不会出现在清单里 ——
    /// 清单反映的是真实状态而不是声明状态，"我以为我关掉了"因此可以被一次查询证伪。
    /// </para>
    /// <para>
    /// ★ <strong>不在清单里 ≠ 这条能力关上了。</strong>控制器可被消费方整体替换，
    /// 挂在它上面的特性会随之失效；真正的判定必须落在服务层。本端点解决可见性，不解决授权。
    /// </para>
    /// </remarks>
    [HttpGet("sensitive-endpoints")]
    public virtual ApiResult<SensitiveEndpointReportDto> GetSensitiveEndpoints(
        [FromServices] IActionDescriptorCollectionProvider actionProvider)
    {
        var endpoints = actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Select(action => new
            {
                Action = action,
                Sensitive = action.MethodInfo.GetCustomAttribute<SensitiveEndpointAttribute>(inherit: true)
            })
            .Where(x => x.Sensitive != null)
            .Select(x => new SensitiveEndpointDto
            {
                Name = x.Sensitive!.Name,
                Reason = x.Sensitive.Reason,
                Route = x.Action.AttributeRouteInfo?.Template ?? string.Empty,
                HttpMethod = x.Action.ActionConstraints?
                    .OfType<HttpMethodActionConstraint>()
                    .FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET",
                Controller = x.Action.ControllerTypeInfo.FullName ?? x.Action.ControllerTypeInfo.Name,
                Module = x.Action.ControllerTypeInfo.Assembly.GetName().Name ?? string.Empty,
                IsDefaultController = x.Action.ControllerTypeInfo
                    .GetCustomAttributes<DefaultControllerAttribute>().Any(),
                // 匿名可达的敏感端点是清单里最该先看的一行。
                AllowsAnonymous = x.Action.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            })
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        return Ok(new SensitiveEndpointReportDto
        {
            TotalCount = endpoints.Count,
            Endpoints = endpoints
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
    /// Get the module health report - dependency integrity and initialization status
    /// across all loaded modules. Read-only; inherits the class-level diagnostics
    /// view permission.
    /// </summary>
    [HttpGet("modules/health")]
    public virtual ApiResult<ModuleHealthReportDto> GetModuleHealth(
        [FromServices] ITnziApplication tnziApp,
        [FromServices] ModuleHealthChecker healthChecker)
    {
        var result = healthChecker.CheckAll(tnziApp.Modules);

        var report = new ModuleHealthReportDto
        {
            IsHealthy = result.IsHealthy,
            IssueCount = result.Issues.Count,
            Issues = result.Issues.Select(i => new ModuleHealthIssueDto
            {
                Module = i.ModuleType.Name,
                IssueType = i.IssueType.ToString(),
                Message = i.Message,
                MissingDependencies = i.MissingDependencies?.Select(t => t.Name).ToList() ?? []
            }).ToList()
        };

        return Ok(report);
    }

    /// <summary>
    /// Get the admin manifest - the subset of loaded modules and admin
    /// controllers shaped for a frontend that wants to auto-render menus
    /// and CRUD pages.
    ///
    /// Read by <c>fetchAdminManifest(client)</c> in <c>@tnzi/ui-admin</c>.
    /// Entity routes are API routes, not admin SPA paths, so they describe the
    /// backend surface rather than menu links. The DTO is intentionally flatter than
    /// <see cref="GetModules"/>: one entity per admin route prefix, with
    /// the HTTP methods that prefix exposes - so the frontend can decide
    /// whether to surface a page (only if route is reachable) and whether
    /// to enable Create/Update/Delete UX (based on supported verbs).
    /// <para>
    /// Routes are reported without the configured <c>AspNetCore:ApiPathPrefix</c>
    /// (<c>"admin/users"</c>, never <c>"api/admin/users"</c>), and an entity's route is the
    /// controller-level template, not the template of one of its actions. Controllers whose
    /// controller-level template carries a route parameter (for example
    /// <c>admin/agents/{agentId}/memory</c>) are nested resources with no page of their own
    /// and are not listed.
    /// </para>
    /// </summary>
    [HttpGet("admin-manifest")]
    public virtual ApiResult<AdminManifestDto> GetAdminManifest(
        [FromServices] ITnziApplication tnziApp,
        [FromServices] IActionDescriptorCollectionProvider actionProvider,
        [FromServices] IOptions<AspNetCoreOptions> aspNetCoreOptions)
    {
        // Index the loaded modules by assembly. One assembly can declare several modules
        // (the core Tnzi assembly declares five), so the index holds every one of them.
        var modulesByAssembly = tnziApp.Modules
            .GroupBy(m => m.Assembly.GetName().Name ?? m.Assembly.FullName ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());

        // RoutePrefixConvention 把 ApiPathPrefix 并进了每个 action 的模板（默认 "api/admin/..."），
        // 判定 admin 控制器之前必须先剥掉它，否则默认配置下一个控制器都匹配不上。
        var apiPrefix = (aspNetCoreOptions.Value.ApiPathPrefix ?? string.Empty).Trim().Trim('/');

        // Group all controller actions by their controller type, then bucket
        // by assembly so we can ascribe them to the owning module.
        var adminControllerGroups = actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Select(a => (Action: a, Template: StripApiPrefix(a.AttributeRouteInfo?.Template, apiPrefix)))
            .Where(x => x.Template.StartsWith(AdminRoutePrefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Action.ControllerTypeInfo.AsType())
            .ToList();

        var perModule = new Dictionary<string, AdminModuleEntryDto>();

        foreach (var ctrlGroup in adminControllerGroups)
        {
            var controllerType = ctrlGroup.Key;
            var assemblyName = controllerType.Assembly.GetName().Name ?? string.Empty;
            if (string.IsNullOrEmpty(assemblyName)) continue;

            // 实体路由取控制器级模板，不取某个 action 的完整模板：后者随 action 枚举顺序变化，
            // 还会把 "admin/diagnostics/exceptions/summary" 这种 action 路径当成实体。
            // 控制器上没有 [Route] 时退到全部 action 模板的公共字面前缀。
            var route = ResolveControllerRoute(controllerType, ctrlGroup.First().Action, apiPrefix)
                        ?? CommonLiteralPrefix(ctrlGroup.Select(x => x.Template));
            if (!route.StartsWith(AdminRoutePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            // 带路由参数的控制器模板是父实体下的子资源，没有可直接导航的页面。
            if (route.Contains('{')) continue;

            // Entity name = everything after "admin/".
            var entityName = ExtractEntityName(route);
            if (string.IsNullOrEmpty(entityName)) continue;

            var isDefault = controllerType.GetCustomAttributes<DefaultControllerAttribute>().Any();

            // Aggregate HTTP methods across all actions of this controller.
            var methods = ctrlGroup
                .Select(x => x.Action)
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
                modulesByAssembly.TryGetValue(assemblyName, out var modules);
                moduleEntry = new AdminModuleEntryDto
                {
                    Name = ExtractModuleShortName(assemblyName),
                    FullName = modules?[0].Type.FullName ?? assemblyName,
                    Assembly = assemblyName,
                    // 同一程序集的多个模块折叠成一行：任一启用即启用（与 admin shell 的折叠规则一致）。
                    IsEnabled = modules?.Any(m => m.IsEnabled) ?? true,
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
        if (route.Length <= AdminRoutePrefix.Length) return string.Empty;
        return route.Substring(AdminRoutePrefix.Length).TrimEnd('/');
    }

    private const string AdminRoutePrefix = "admin/";

    /// <summary>
    /// Remove the configured API path prefix (already trimmed of slashes, e.g. <c>"api"</c>)
    /// from a route template. Templates that do not carry it (controllers marked
    /// <see cref="NonRoutePrefixAttribute"/>, or an empty prefix) come back unchanged.
    /// </summary>
    private static string StripApiPrefix(string? template, string apiPrefix)
    {
        var normalized = NormalizeTemplate(template);
        if (apiPrefix.Length == 0) return normalized;

        if (normalized.StartsWith(apiPrefix + "/", StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(apiPrefix.Length + 1);

        return normalized.Equals(apiPrefix, StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
    }

    /// <summary>
    /// Route template without the leading <c>/</c> or <c>~/</c> and trailing slashes.
    /// </summary>
    private static string NormalizeTemplate(string? template)
    {
        var normalized = (template ?? string.Empty).Trim();
        if (normalized.StartsWith("~/", StringComparison.Ordinal)) normalized = normalized.Substring(2);
        return normalized.Trim('/');
    }

    /// <summary>
    /// The controller-level admin route template, read the way MVC reads it: the route
    /// attributes of the most derived class in the hierarchy that declares any, so a
    /// subclass with its own <c>[Route]</c> replaces the inherited one.
    /// </summary>
    private static string? ResolveControllerRoute(Type controllerType, ControllerActionDescriptor action, string apiPrefix)
    {
        for (var type = controllerType; type != null && type != typeof(object); type = type.BaseType)
        {
            var routes = type.GetCustomAttributes<RouteAttribute>(inherit: false).ToList();
            if (routes.Count == 0) continue;

            foreach (var routeAttribute in routes)
            {
                var template = AttributeRouteModel.ReplaceTokens(routeAttribute.Template, action.RouteValues);
                var route = StripApiPrefix(template, apiPrefix);
                if (route.StartsWith(AdminRoutePrefix, StringComparison.OrdinalIgnoreCase)) return route;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// The longest run of leading literal segments shared by all templates, stopping at the
    /// first segment that holds a route parameter.
    /// </summary>
    private static string CommonLiteralPrefix(IEnumerable<string> templates)
    {
        List<string>? common = null;
        foreach (var template in templates)
        {
            var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .TakeWhile(s => !s.Contains('{'))
                .ToList();
            if (common == null)
            {
                common = segments;
                continue;
            }

            var shared = 0;
            while (shared < common.Count && shared < segments.Count
                   && string.Equals(common[shared], segments[shared], StringComparison.OrdinalIgnoreCase))
            {
                shared++;
            }

            common = common.Take(shared).ToList();
        }

        return string.Join('/', common ?? []);
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

/// <summary>
/// Module health report - dependency integrity and initialization status.
/// Serialization shape for <c>GET /admin/diagnostics/modules/health</c>.
/// </summary>
public class ModuleHealthReportDto
{
    /// <summary>Whether all loaded modules are healthy (no issues detected).</summary>
    public bool IsHealthy { get; set; }

    /// <summary>Total number of detected health issues.</summary>
    public int IssueCount { get; set; }

    /// <summary>The detected health issues (empty when healthy).</summary>
    public List<ModuleHealthIssueDto> Issues { get; set; } = [];
}

/// <summary>
/// A single module health issue (missing dependency, not initialized, or initialization failed).
/// </summary>
public class ModuleHealthIssueDto
{
    /// <summary>Simple name of the module the issue belongs to.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Issue category (MissingDependency / NotInitialized / InitializationFailed).</summary>
    public string IssueType { get; set; } = string.Empty;

    /// <summary>Human-readable description of the issue.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Simple names of the missing dependency modules (empty unless a dependency issue).</summary>
    public List<string> MissingDependencies { get; set; } = [];
}

/// <summary>
/// The sensitive endpoints that are actually reachable in this process.
/// </summary>
public class SensitiveEndpointReportDto
{
    /// <summary>Number of sensitive endpoints currently active.</summary>
    public int TotalCount { get; set; }

    /// <summary>The endpoints, ordered by capability name.</summary>
    public List<SensitiveEndpointDto> Endpoints { get; set; } = [];
}

/// <summary>
/// One active sensitive endpoint.
/// </summary>
public class SensitiveEndpointDto
{
    /// <summary>Capability name; also the key used to suppress it via ControllerFilter:DisabledEndpoints.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Why this endpoint is sensitive, stated as a consequence.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Route template as registered.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>Primary HTTP method.</summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>Declaring controller type.</summary>
    public string Controller { get; set; } = string.Empty;

    /// <summary>Assembly the controller comes from.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>True when the controller is a framework default that was activated automatically.</summary>
    public bool IsDefaultController { get; set; }

    /// <summary>True when the endpoint is reachable without authentication.</summary>
    public bool AllowsAnonymous { get; set; }
}
