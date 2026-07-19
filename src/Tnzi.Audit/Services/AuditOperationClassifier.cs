using Microsoft.AspNetCore.Mvc.Controllers;

namespace Tnzi.Audit.Services;

/// <summary>
/// 操作审计的写/读分类器 + 端点权限码提取：在**采集时**（AuditMiddleware）对请求定案，
/// 结果持久化到 <c>AuditOperation.IsWrite</c> / <c>AuditOperation.PermissionName</c>。
/// <para>
/// 判定优先级：
/// ① <c>[AuditRead]</c> 显式声明（伪读端点的权威出口，与路由/方法名解耦）；
/// ② 方法级操作权限码（<c>.create/.update/.delete/.execute</c>）= 权威写声明
///   （三层权限门约定里类级恒为 <c>.view</c>，操作码只出现在写端点；也覆盖非 admin 面的
///   <c>*.execute</c> 类授权码）；
/// ③ 三层门约定的 admin 面（<c>ApiAdminControllerBase</c> 且类级 <c>.view</c> 码在场）
///   按最具体权限码归类：仍是 <c>.view</c> = 读——约定要求 admin 写端点 MUST 带方法级
///   操作码且有全框架约定测试强制；非 <c>.view</c> 的方法级动作门（标准 CRUD 后缀之外的
///   语义化操作码，如 <c>authorization.roleFunction.assign</c>）= 写，但仅对写 HTTP 方法
///   生效——GET 面的动作门（如 <c>finance.eft.download</c>）仍是读。
///   类级 .view 码是"该端点采用约定"的证据，未采用约定的 admin 面（粗粒度自定义码）
///   落回启发式，不做此推断；
/// ④ 回退：HTTP 方法 + 既有伪读启发式（<c>POST .../query</c> 路由惯例、<c>.Get</c> 方法名）。
/// </para>
/// 查询端只对 <c>IsWrite=null</c> 的历史行回退旧启发式
/// （<c>AuditOperationService.ApplyQueryFilters</c>），新行从此不再依赖字符串猜测。
/// </summary>
internal static class AuditOperationClassifier
{
    private static readonly string[] OperationCodeSuffixes = [".create", ".update", ".delete", ".execute"];

    private static readonly HashSet<string> WriteHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    /// <summary>对当前请求定案写/读分类，并提取用于持久化的端点权限码。</summary>
    internal static (bool IsWrite, string? PermissionName) Classify(HttpContext context, string functionName)
    {
        var endpoint = context.GetEndpoint();

        var permissionNames = endpoint?.Metadata
            .GetOrderedMetadata<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .OfType<string>()
            .ToArray() ?? [];

        // 方法级操作码（三层门约定下只声明在写端点；类级恒为 .view）
        var operationCode = Array.Find(permissionNames, n =>
            OperationCodeSuffixes.Any(s => n.EndsWith(s, StringComparison.OrdinalIgnoreCase)));

        // 持久化用权限码：操作码最具体；否则取最后一个
        // （EndpointMetadataCollection 中 action 级排在 controller 级之后，越靠后越具体）
        var permissionName = operationCode ?? permissionNames.LastOrDefault();

        // ① 显式读声明
        if (endpoint?.Metadata.GetMetadata<AuditReadAttribute>() != null)
        {
            return (false, permissionName);
        }

        // ② 方法级操作权限码 = 权威写声明
        if (operationCode != null)
        {
            return (true, permissionName);
        }

        // ③ 三层权限门约定的 admin 面：类级 .view 码在场（证明该端点采用三层门约定，
        //    约定下写端点必带方法级操作码且有全框架约定测试强制）且无标准操作码。
        //    按最具体的码归类（action 级排在 controller 级之后）：仍是 .view = 读；
        //    非 .view 的方法级动作门（标准 CRUD 后缀之外的语义化操作码：
        //    authorization.roleFunction.assign、消费应用的 .settle/.approve 等）只声明在
        //    写端点上 = 写——但仅对写 HTTP 方法生效，GET 面的动作门（finance.eft.download）
        //    仍是读。确需非 .view 方法级门的读 POST 用 [AuditRead] 显式声明。
        //    未采用约定的 admin 面（粗粒度自定义码/无码，如部分消费应用）不适用——
        //    落回启发式，避免把"漏声明操作码的写端点"误判为读。
        var hasViewCode = permissionNames.Any(n => n.EndsWith(".view", StringComparison.OrdinalIgnoreCase));
        var controllerType = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo;
        if (hasViewCode && controllerType != null && typeof(ApiAdminControllerBase).IsAssignableFrom(controllerType))
        {
            var hasActionGate = !permissionNames[^1].EndsWith(".view", StringComparison.OrdinalIgnoreCase);
            return (hasActionGate && WriteHttpMethods.Contains(context.Request.Method), permissionName);
        }

        // ④ 回退：HTTP 方法 + 伪读启发式
        var method = context.Request.Method;
        if (!WriteHttpMethods.Contains(method))
        {
            return (false, permissionName);
        }

        var isPseudoRead = HttpMethods.IsPost(method)
            && (context.Request.Path.Value?.EndsWith("/query", StringComparison.OrdinalIgnoreCase) == true
                || functionName.Contains(".Get", StringComparison.Ordinal));

        return (!isPseudoRead, permissionName);
    }
}
