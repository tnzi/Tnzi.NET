namespace Tnzi.Authorization.Security;

/// <summary>
/// 功能授权处理器
/// </summary>
/// <remarks>
/// 一个端点上的全部 <see cref="FunctionAuthorizationRequirement"/>（基类裸门 + 类级 <c>.view</c> + 方法级操作码）
/// 一起判定：带码的那几道走一次 <see cref="IFunctionAuthorizationService.CheckPermissionsAsync"/>。
/// 逐道判定时每道码各做一次超管判定，而超管判定是一次刻意不缓存的角色查询（改名、删除与成员变更要立即生效），
/// 写端点因此每个请求多一次数据库往返。判定结果与逐道判定逐字相同：每道码各自成立才算通过。
/// </remarks>
public class FunctionAuthorizationHandler : AuthorizationHandler<FunctionAuthorizationRequirement>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationHandler"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationHandler(IServiceProvider serviceProvider, ICurrentUser currentUser)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _currentUser = Check.NotNull(currentUser);
    }

    /// <summary>
    /// 一次处理上下文里全部的功能授权要求。
    /// </summary>
    /// <param name="context">授权上下文</param>
    public override Task HandleAsync(AuthorizationHandlerContext context)
    {
        var requirements = context.Requirements.OfType<FunctionAuthorizationRequirement>().ToList();
        return requirements.Count == 0 ? Task.CompletedTask : EvaluateAsync(context, requirements);
    }

    /// <summary>
    /// 处理单个授权要求（与 <see cref="HandleAsync"/> 同一套判定）。
    /// </summary>
    /// <param name="context">授权上下文</param>
    /// <param name="requirement">授权要求</param>
    /// <returns>授权结果</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FunctionAuthorizationRequirement requirement)
        => EvaluateAsync(context, [requirement]);

    private async Task EvaluateAsync(AuthorizationHandlerContext context, IReadOnlyList<FunctionAuthorizationRequirement> requirements)
    {
        var httpContext = _serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return;
        }

        var endpoint = httpContext.GetEndpoint();

        // 检查是否有 AllowAnonymous 特性
        var hasAllowAnonymous = endpoint?.Metadata
            .OfType<AllowAnonymousAttribute>()
            .Any() ?? false;
        if (hasAllowAnonymous)
        {
            foreach (var requirement in requirements)
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 检查用户是否已认证
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return;
        }

        // 未显式指定权限的要求（[ApiAuthorize] 裸门）只验证登录，不校验具体权限；
        // 带权限名的（ApiAuthorize(PermissionName="xxx")）留待下面一起判定。
        var named = new List<FunctionAuthorizationRequirement>();
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrEmpty(requirement.PermissionName))
            {
                context.Succeed(requirement);
            }
            else
            {
                named.Add(requirement);
            }
        }

        if (named.Count == 0)
        {
            return;
        }

        // 验证权限
        var userId = _currentUser.Id;
        if (userId == null)
        {
            context.Fail();
            return;
        }

        var authorizationService = _serviceProvider.GetRequiredService<IFunctionAuthorizationService>();
        var codes = named.Select(r => r.PermissionName!).Distinct(StringComparer.Ordinal).ToList();

        // 只有一道码时走单查，多道码时一次批量判定（一次超管判定 + 一次权限集读取）。
        IReadOnlyDictionary<string, bool> verdicts = codes.Count == 1
            ? new Dictionary<string, bool> { [codes[0]] = await authorizationService.CheckPermissionAsync(userId.Value, codes[0]) }
            : await authorizationService.CheckPermissionsAsync(userId.Value, codes);

        foreach (var requirement in named)
        {
            if (verdicts.TryGetValue(requirement.PermissionName!, out var granted) && granted)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }
        }
    }
}
