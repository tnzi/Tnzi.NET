namespace Tnzi.AI.Agents;

/// <summary>
/// 子 Agent 类型注册表 - 管理可用的子 Agent 类型定义
/// </summary>
/// <remarks>
/// 注册表是进程级单例，而 <see cref="SubAgentType"/> 按租户存放（唯一索引 TenantId + Name），
/// 故数据库来源的类型<b>按租户分桶</b>：内置类型与经 <see cref="Register"/> 注册的类型是全局的，
/// 每个租户桶只装本租户 <see cref="LoadTenantFromStoreAsync"/> 读到的行，读取时「本租户桶 ∪ 全局」、同名以租户桶为准。
/// 不带租户键的旧成员读写 <see cref="SubAgentTenantKey.Default"/> 桶（TenantId 为 null 的行，单租户部署逐字不变）。
/// 三个带租户键的成员是默认接口成员，自定义实现不实现它们时退回旧成员（即不分桶）。
/// </remarks>
public interface ISubAgentRegistry
{
    /// <summary>获取所有已注册的子 Agent 类型（全局 ∪ 默认桶）</summary>
    IReadOnlyList<SubAgentTypeDefinition> GetAll();

    /// <summary>按名称获取子 Agent 类型（默认桶优先于全局；不存在返回 null）</summary>
    SubAgentTypeDefinition? Get(string name);

    /// <summary>注册全局自定义子 Agent 类型（同名覆盖；不随任何租户的重载消失）</summary>
    void Register(SubAgentTypeDefinition definition);

    /// <summary>取消注册全局子 Agent 类型</summary>
    bool Unregister(string name);

    /// <summary>从数据库加载已启用的子 Agent 类型定义到默认桶（整桶替换；同名覆盖内置类型）</summary>
    Task LoadFromStoreAsync(IRepository<SubAgentType, Guid> repository, CancellationToken cancellationToken = default);

    /// <summary>获取某租户可见的全部子 Agent 类型（全局 ∪ 该租户桶，同名以租户桶为准）</summary>
    IReadOnlyList<SubAgentTypeDefinition> GetAllForTenant(string tenantKey) => GetAll();

    /// <summary>按名称获取某租户可见的子 Agent 类型（租户桶优先于全局；不存在返回 null）</summary>
    SubAgentTypeDefinition? GetForTenant(string name, string tenantKey) => Get(name);

    /// <summary>
    /// 从数据库加载已启用的子 Agent 类型定义到 <paramref name="tenantKey"/> 桶（只替换该桶）。
    /// <paramref name="repository"/> 须来自该租户的作用域：全局租户过滤器决定读到哪些行。
    /// </summary>
    Task LoadTenantFromStoreAsync(IRepository<SubAgentType, Guid> repository, string tenantKey, CancellationToken cancellationToken = default)
        => LoadFromStoreAsync(repository, cancellationToken);

    /// <summary>
    /// 启动期整体装载：读到的每一行按 <see cref="SubAgentType.TenantId"/> 分进各自的桶（TenantId 为 null 进默认桶）。
    /// 调用方须先关掉多租户过滤器（<c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;()</c>），否则根作用域只读得到默认桶的行，
    /// 各租户的类型要等到该租户管理员下一次 CRUD 才出现。
    /// </summary>
    Task LoadAllTenantsFromStoreAsync(IRepository<SubAgentType, Guid> repository, CancellationToken cancellationToken = default)
        => LoadFromStoreAsync(repository, cancellationToken);
}

/// <summary>
/// 子 Agent 注册表的租户桶键：<see cref="ICurrentTenant.Id"/> 的字符串形式，无租户时为 <see cref="Default"/>
/// （与 <c>McpServerCatalog</c> 的快照键同口径）。
/// </summary>
public static class SubAgentTenantKey
{
    /// <summary>无租户（TenantId 为 null 的行、单租户部署、启动期根作用域）</summary>
    public const string Default = "__default";

    /// <summary>由租户 Id 得出桶键</summary>
    public static string From(Guid? tenantId) => tenantId?.ToString() ?? Default;
}
