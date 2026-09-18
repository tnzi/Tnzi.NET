namespace Tnzi.Identity.Organization;

/// <summary>
/// Identity 组织架构子模块：用户挂靠的那棵「公司形状」—— 组织层级、组织代码、排序、
/// 每个节点的人数统计，以及「把某个用户放进某个节点」。
/// </summary>
/// <remarks>
/// <b>业务范围一句话</b>：回答「这个人属于公司里的哪一块」。
/// <br/><br/>
/// <b>谁会刻意只加载 Identity 而不加载本模块</b>：用户是自然人的产品（一人一账号，
/// 彼此之间没有隶属关系），以及主体是服务而不是人的无头 API（机器账号谈不上部门）。
/// 这两类宿主同样需要认证、令牌、会话、2FA、角色，但一棵组织树对他们是纯粹的噪音：
/// 一张表、17 个端点、4 个权限码、一页永远打不开的管理界面。
/// <br/><br/>
/// <b>缺席退化成什么</b>：**少一棵组织树，不改任何一条身份认证行为**。登录、令牌签发、
/// 会话、2FA、passkey、密码策略、角色授权、登录日志与登录安全**一个字节不差**。
/// 可感知的差异只有四处：①<c>admin/organizations</c> 整组端点不存在（管理端菜单经
/// <c>moduleGate: 'identity-organization'</c> 一并隐藏，不会渲染死链）；
/// ②<c>admin/users/{id}/assign-organization</c> 与 <c>remove-organization</c> 答 <b>501</b>
/// 并指名要加载哪个包（不是 503：503 意味着「暂时坏了，等会再试」，会让监控和客户端
/// 一直重试一件永远不会恢复的事）；③用户 DTO 的 <c>OrganizationName</c> 恒为 null
/// （<c>OrganizationId</c> 列还在，只是没人能把它翻译成名字）；④这 4 个权限码不 seed。
/// <br/><br/>
/// <b>依赖方向：本模块 → 核心，恒定单向</b>。核心持有的是**契约**
/// <see cref="IOrganizationService"/> 与那组 DTO（可空可选注入），实现在本模块 ——
/// 与 Finance 的 <c>ICheckDocumentRenderer</c> / <c>IReceiptExtractor</c> 同一形状。
/// 反过来本模块引用核心的 <c>User</c> / <c>UserManager</c> / <c>UserListItemDto</c>，
/// 都是「子 → 父」，合法。
/// <br/><br/>
/// <b>表前缀沿用 <c>Identity</c></b>：拆的是程序集不是 schema，表名一字不变，
/// 因此**不产生任何迁移**。前缀是按实体所在程序集在模块容器里查的
/// （<c>TableNamePrefixConfiguration</c>），少写下面那一行，这张表会**一声不吭地**掉掉前缀。
/// 由 <c>Tnzi.Identity.Organization.Tests/TableNamingTests</c> 守着（走框架真的用的那段解析代码）。
/// <br/><br/>
/// 与 <c>Tnzi.Identity.Presence</c> 同形态：共享父前缀、<c>[DependsOn(IdentityModule)]</c>、
/// 父模块对它零引用。
/// </remarks>
[DependsOn(typeof(IdentityModule))]
public class IdentityOrganizationModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与父模块共享前缀：拆程序集不改表名，零迁移。</remarks>
    public override string? TableNamePrefix => IdentityConstants.TablePrefix;

    /// <inheritdoc />
    /// <remarks>
    /// 6：Identity(0) 与 Identity.Presence(5) 之后。实际次序由 <c>[DependsOn]</c> 拓扑排序
    /// 保证，此值仅为同级 tiebreak。
    /// </remarks>
    public override int LoadOrder => 6;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：没有组织架构的宿主不会 seed 这 4 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, IdentityOrganizationPermissions>();

        // 契约在核心（Tnzi.Identity），实现在这里。核心只在 UserService /
        // DefaultUserAdminController 里按可空可选依赖持有它，未加载本模块时那两处降级。
        context.Services.AddScoped<IOrganizationService, OrganizationService>();

        // 存量修复：把旧写法留下的「随机 GUID 路径段」重建成祖先 Id 链（迁移之后、每次启动、幂等）。
        context.Services.AddTransient<IPostMigrationStartupTask, OrganizationPathRepairStartupTask>();

        return base.ConfigureServicesAsync(context);
    }
}
