namespace Tnzi.AI.Engine;

/// <summary>
/// <see cref="IUserToolPermissionResolver"/> 的框架实现：按注册表收集所需权限并集，再按请求携带的用户逐一检查。
/// </summary>
public class UserToolPermissionResolver : IUserToolPermissionResolver
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<UserToolPermissionResolver> _logger;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;

    public UserToolPermissionResolver(
        IToolRegistry toolRegistry,
        ILogger<UserToolPermissionResolver> logger,
        IPermissionChecker? permissionChecker = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        _toolRegistry = Check.NotNull(toolRegistry);
        _logger = Check.NotNull(logger);
        _permissionChecker = permissionChecker;
        _executionContextAccessor = executionContextAccessor;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>?> ResolveAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? toolNames, CancellationToken ct)
    {
        if (toolGroups == null && toolNames == null) return null;

        // 收集工具组 + 单工具两路声明的权限要求（按工具名去重，再展开权限）。
        // 这两处刻意不传 userPermissions：这里要的是「有哪些工具声明了权限」的全集，门控在下游做。
        var requiredPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (toolGroups != null)
        {
            foreach (var t in _toolRegistry.GetToolsByGroups(toolGroups))
                foreach (var p in t.RequiredPermissions)
                    requiredPermissions.Add(p);
        }
        if (toolNames != null)
        {
            foreach (var t in _toolRegistry.GetToolsByNames(toolNames))
                foreach (var p in t.RequiredPermissions)
                    requiredPermissions.Add(p);
        }

        if (requiredPermissions.Count == 0) return null;

        // 有门控工具却没有权限检查器（Authorization 模块未加载）：失败关闭，空集 ⇒ 门控工具全部排除。
        // 返回 null 等于放行全部门控工具。
        if (_permissionChecker == null)
        {
            _logger.LogWarning(
                "Tools requiring permissions ({Permissions}) were requested but no IPermissionChecker is registered; " +
                "all permission-gated tools are excluded. Load the Authorization module to enable them.",
                string.Join(", ", requiredPermissions));
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // 逐一检查权限，构建已授权集合。
        // 身份取请求携带的 UserId（与 ApprovalToolWrapper / 权限规则评估同源）：后台子 Agent 在新作用域
        // 新执行流里跑，环境里没有当前用户，按环境查会让 spawn 出来的运行一律丢掉全部门控工具。
        // 请求没带 UserId 时退回环境用户（HTTP 作用域内消费方自建的请求）。
        var requestUserId = _executionContextAccessor?.CurrentRequest?.UserId;
        var grantedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in requiredPermissions)
        {
            ct.ThrowIfCancellationRequested();
            var granted = requestUserId.HasValue
                ? await _permissionChecker.IsGrantedAsync(requestUserId.Value, permission)
                : await _permissionChecker.IsGrantedAsync(permission);
            if (granted)
            {
                grantedPermissions.Add(permission);
            }
        }

        return grantedPermissions;
    }
}
