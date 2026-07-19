namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 可选的泛型 admin CRUD 控制器基类。把标准 增删改查端点收口为一处，转发到
/// <see cref="ICrudAppService{TKey,TDto,TCreateDto,TUpdateDto}"/> 并 <c>.ToApiResult()</c>。
/// </summary>
/// <remarks>
/// <para><b>opt-in</b>：继承本类纯属可选，现有手写控制器不受影响。</para>
/// <para><b>三层门只声明一次</b>：认证边界由 <see cref="ApiAdminControllerBase"/> 的裸
/// <c>[ApiAuthorize]</c> 提供；<b>面级 <c>.view</c></b> 与 <b>动作级 <c>.create</c>/<c>.update</c>/<c>.delete</c></b>
/// 由本基类在每个端点内按 <see cref="PermissionPrefix"/> 组装并<b>命令式</b>强制
/// （<see cref="RequirePermissionAsync"/> → <see cref="Tnzi.Security.Authorization.IPermissionChecker.CheckAsync"/>，
/// 超管旁路等语义与属性式 <c>[ApiAuthorize]</c> 同源）。因权限码在运行时由前缀拼装、
/// 静态特性无法读取运行时属性，故采用命令式校验而非方法级 <c>[ApiAuthorize]</c> 特性——
/// 语义与 <c>docs/coding-standards/controller.md</c> 的三层门一致（读端点仅面级 <c>.view</c>，
/// 写端点叠加动作级码）。</para>
/// <para>派生控制器只需：给出 <c>[Route("admin/...")]</c> + <c>[ApiExplorerSettings]</c>、实现
/// <see cref="PermissionPrefix"/>、把具体 service 传给构造函数。需要覆写某端点行为时 override 对应 <c>virtual</c> 方法。</para>
/// </remarks>
/// <typeparam name="TKey">主键类型</typeparam>
/// <typeparam name="TDto">读取/返回 DTO</typeparam>
/// <typeparam name="TCreateDto">创建输入 DTO</typeparam>
/// <typeparam name="TUpdateDto">更新输入 DTO</typeparam>
[ExperimentalApi(Reason = "Generic CRUD admin controller base is an opt-in convenience surface still stabilising")]
public abstract class CrudAdminControllerBase<TKey, TDto, TCreateDto, TUpdateDto> : ApiAdminControllerBase
    where TKey : notnull
{
    /// <summary>底层 CRUD 应用服务。</summary>
    protected ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto> Service { get; }

    /// <summary>
    /// 初始化泛型 admin CRUD 控制器基类。
    /// </summary>
    /// <param name="service">底层 CRUD 应用服务</param>
    /// <param name="serviceProvider">服务提供者（可选）</param>
    protected CrudAdminControllerBase(
        ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto> service,
        IServiceProvider? serviceProvider = null)
        : base(serviceProvider)
    {
        Service = Check.NotNull(service);
    }

    /// <summary>
    /// 权限码前缀（如 <c>"catalog.product"</c>）。三层门据此组装：面级 <c>.view</c> + 写端点 <c>.create</c>/<c>.update</c>/<c>.delete</c>。
    /// </summary>
    protected abstract string PermissionPrefix { get; }

    /// <summary>面级读取权限码。</summary>
    protected virtual string ViewPermission => $"{PermissionPrefix}.view";

    /// <summary>创建权限码。</summary>
    protected virtual string CreatePermission => $"{PermissionPrefix}.create";

    /// <summary>更新权限码。</summary>
    protected virtual string UpdatePermission => $"{PermissionPrefix}.update";

    /// <summary>删除权限码。</summary>
    protected virtual string DeletePermission => $"{PermissionPrefix}.delete";

    /// <summary>分页查询（复杂过滤走 POST body，携 <see cref="PagedQuery.Filter"/>）。</summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<TDto>>> Query([FromBody] PagedQuery query, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(ViewPermission);
        return (await Service.QueryAsync(query, cancellationToken)).ToApiResult();
    }

    /// <summary>分页查询（简单条件走查询串）。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<TDto>>> GetList([FromQuery] PagedQueryDto query, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(ViewPermission);
        return (await Service.QueryAsync(query, cancellationToken)).ToApiResult();
    }

    /// <summary>按主键取详情。</summary>
    [HttpGet("{id}")]
    public virtual async Task<ApiResult<TDto>> GetById(TKey id, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(ViewPermission);
        return (await Service.GetByIdAsync(id, cancellationToken)).ToApiResult();
    }

    /// <summary>新建。</summary>
    [HttpPost]
    public virtual async Task<ApiResult<TDto>> Create([FromBody] TCreateDto input, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(CreatePermission);
        return (await Service.CreateAsync(input, cancellationToken)).ToApiResult();
    }

    /// <summary>更新。</summary>
    [HttpPut("{id}")]
    public virtual async Task<ApiResult<TDto>> Update(TKey id, [FromBody] TUpdateDto input, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(UpdatePermission);
        return (await Service.UpdateAsync(id, input, cancellationToken)).ToApiResult();
    }

    /// <summary>按主键删除。</summary>
    [HttpDelete("{id}")]
    public virtual async Task<ApiResult> Delete(TKey id, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(DeletePermission);
        return (await Service.DeleteAsync(id, cancellationToken)).ToApiResult();
    }

    /// <summary>批量删除（范围外 id 由服务层静默跳过）。</summary>
    [HttpPost("batch-delete")]
    public virtual async Task<ApiResult> BatchDelete([FromBody] IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(DeletePermission);
        return (await Service.BatchDeleteAsync(ids, cancellationToken)).ToApiResult();
    }

    /// <summary>
    /// 命令式权限门：解析 <see cref="Tnzi.Security.Authorization.IPermissionChecker"/> 并校验指定权限码，
    /// 无权限抛 <see cref="Tnzi.Exceptions.ForbiddenException"/>（由异常中间件转 403 信封）。
    /// 授权模块未加载（无权限检查器）时同样拒绝——与 deny-by-default 一致。
    /// </summary>
    protected virtual async Task RequirePermissionAsync(string permissionCode)
    {
        Check.NotNullOrWhiteSpace(permissionCode);

        var checker = Services.GetService<IPermissionChecker>();
        if (checker == null)
            throw new ForbiddenException(
                "Permission checker is not available. Please ensure the Authorization module is loaded.",
                FORBIDDEN);

        await checker.CheckAsync(permissionCode);
    }
}
