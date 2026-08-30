namespace Tnzi.Authorization.DataAuth;

/// <summary>
/// 行级数据授权子模块：决定一个主体能看到<b>哪几行</b>，而不是能进哪些端点。
/// </summary>
/// <remarks>
/// <b>业务范围</b>：实体登记表（<c>EntityInfo</c>）、按角色配置的数据范围（<c>EntityRole</c>），
/// 以及把范围编译成 <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c> 挂到查询上的服务与扩展方法。
/// <br/><br/>
/// <b>谁会刻意只加载父模块</b>：绝大多数应用。功能级授权（权限判定运行时、策略提供程序、
/// 权限码目录、角色授权与用户直授）是 <c>Tnzi.Authorization</c> 存在的理由，
/// 而行级数据范围是<b>可选能力</b> —— 只有「同一张表，销售只看自己的单、区域经理看整片区」
/// 这类需求才用得上。不需要它的宿主不该凭空多出两张表、十五个端点和四个权限码，
/// 也不该在权限矩阵里多出一整块用不上的功能面。
/// <br/><br/>
/// <b>表前缀沿用 <c>Auth_</c></b>：拆的是程序集不是 schema。前缀按<b>实体所在程序集</b>
/// 到模块容器里反查（见 <c>TableNamePrefixConfiguration</c>），所以这一行必须逐字重声明 ——
/// 少了它，<c>Auth_EntityInfo</c> / <c>Auth_EntityRole</c> 会一声不响地变成 <c>EntityInfo</c> /
/// <c>EntityRole</c>。声明之后两张表的表名一字不变，因此<b>不产生任何迁移</b>。
/// <br/><br/>
/// <b>依赖方向单向</b>：本模块依赖 <c>AuthorizationModule</c>（数据范围按角色配置，
/// 用户的角色集经 Identity 的 <c>IUserRoleService</c> 解析），父模块不引用本模块任何类型 ——
/// 拆分前父模块对这一簇只有一处引用，就是 <c>ConfigureServicesAsync</c> 里的那行 DI 注册。
/// 因此没有留在父模块的契约，也没有需要可选注入的回退实现。
/// <br/><br/>
/// <b>缺席时的行为</b>：<c>IDataAuthService</c> 不注册、两张表不建、<c>admin/data-auth</c> 的
/// 十五个端点不存在、四个 <c>authorization.entityRole.*</c> 码不 seed。
/// 功能级授权<b>逐字不变</b>：父模块从不查询数据范围，权限判定的结果与拆分前完全相同。
/// 行级过滤是消费方<b>显式调用</b>才发生的（<c>repository.WithDataAuthAsync(...)</c> 或
/// <c>IDataAuthService.GetDataFilterAsync</c>），所以「忘了加载本包」的表现是<b>调用点编译不过</b>，
/// 不是过滤器安静消失、所有人看见全表 —— 后者才是这条拆分线唯一能造出的错误行为，
/// 而它被编译期挡住了。
/// </remarks>
[DependsOn(typeof(AuthorizationModule))]
public class AuthorizationDataAuthModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与父模块共享前缀：拆程序集不改表名，零迁移。</remarks>
    public override string? TableNamePrefix => "Auth";

    /// <inheritdoc />
    /// <remarks>11：紧随 Authorization(10)。实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</remarks>
    public override int LoadOrder => 11;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 实体与配置类经 IEntityRegister 自动发现，无需在此登记 DbSet。

        services.AddScoped<IDataAuthService, DataAuthService>();

        // 权限码随模块走：不加载本模块的宿主永远不会 seed 这 4 个码。
        // 码串与拆分前逐字相同，父组仍是 Authorization 声明的 authorization。
        services.AddTransient<IPermissionDefinitionProvider, AuthorizationDataAuthPermissions>();

        return Task.CompletedTask;
    }
}
