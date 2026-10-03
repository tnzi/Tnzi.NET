namespace Tnzi.AI.Agents;

/// <summary>
/// 宿主级共享 Agent 定义（YAML 同步而来的 Agent）对调用者的可见性规则。
/// </summary>
/// <remarks>
/// <para>
/// YAML 定义由无租户的后台同步服务写入，行上 <c>TenantId</c> 为 null。<see cref="Agent"/> 是
/// <c>IMultiTenant</c>，全局过滤器严格等值（没有 host / global 租户），多租户一开，任何租户都解析不到、
/// 也列不出这些 Agent。它们是部署方随应用发布的定义，语义上属于宿主、对各租户<b>只读共享</b>：
/// 租户能解析、列出、克隆，不能修改或删除（改了也会在下次同步被 YAML 覆盖）。
/// </para>
/// <para>
/// 规则只在<b>多租户开启且调用者带租户</b>时生效，此时可见集合 = 本租户的行 ∪ 共享定义
/// （<c>Source=yaml</c> 且 <c>TenantId</c> 为 null）。单租户部署与宿主上下文里查询原样返回，
/// 一行谓词都不加 —— 多租户关闭时 <c>TenantId</c> 被 <c>Ignore</c>，引用它会让查询翻译失败。
/// </para>
/// <para>
/// 与 <c>SkillEntity</c> / <c>Provider</c> 的「服务层联合过滤」同一思路，但 <see cref="Agent"/> 保留
/// <c>IMultiTenant</c>（写入仍由框架自动打租户戳，其余几十处读取仍由全局过滤器隔离），只在需要
/// 看见共享定义的几个读取点摘掉全局过滤器、手工补回软删与租户条件。刻意不用
/// <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;</c>：它每次调用记一条跨租户 Warning，
/// 而这里是每一轮对话都要走的热路径。
/// </para>
/// </remarks>
internal readonly struct SharedAgentScope
{
    private SharedAgentScope(Guid? tenantId)
    {
        CallerTenantId = tenantId;
    }

    /// <summary>调用者所属租户；多租户关闭 / 宿主上下文时为 null。</summary>
    public Guid? CallerTenantId { get; }

    /// <summary>调用者是否被钉在某个租户上（只有此时共享定义才需要额外放行）。</summary>
    public bool IsTenantCaller => CallerTenantId.HasValue;

    /// <summary>
    /// 解析调用者的作用域。租户由身份决定（<see cref="ICurrentTenant"/> 优先，退回当前用户的租户 claim），
    /// 与全局过滤器同源；多租户开关缺席按关闭。
    /// </summary>
    public static SharedAgentScope Resolve(IOptions<MultiTenancyOptions>? multiTenancyOptions, ICurrentTenant? currentTenant, ICurrentUser? currentUser)
    {
        var enabled = multiTenancyOptions?.Value.Enabled ?? false;
        return enabled
            ? new SharedAgentScope(currentTenant?.Id ?? currentUser?.TenantId)
            : default;
    }

    /// <summary>从服务容器解析（供没有构造注入入口的静态路径使用）。</summary>
    public static SharedAgentScope Resolve(IServiceProvider serviceProvider)
    {
        Check.NotNull(serviceProvider);
        return Resolve(
            serviceProvider.GetService<IOptions<MultiTenancyOptions>>(),
            serviceProvider.GetService<ICurrentTenant>(),
            serviceProvider.GetService<ICurrentUser>());
    }

    /// <summary>一行 Agent 是否为宿主级共享定义。</summary>
    public static bool IsSharedDefinition(Agent agent)
        => agent.Source == AgentSources.Yaml && agent.TenantId == null;

    /// <summary>该 Agent 对调用者是否只读（租户调用者面对共享定义）。</summary>
    public bool IsReadOnlyFor(Agent agent) => IsTenantCaller && IsSharedDefinition(agent);

    /// <summary>
    /// Agent 查询的可见集合：租户调用者 = 本租户 ∪ 共享定义；其余情况原样返回。
    /// </summary>
    public IQueryable<Agent> Apply(IQueryable<Agent> agents)
    {
        Check.NotNull(agents);
        if (!IsTenantCaller)
        {
            return agents;
        }

        var tenantId = CallerTenantId;
        return agents
            .IgnoreQueryFilters()
            .Where(a => !a.IsDeleted
                        && (a.TenantId == tenantId
                            || (a.TenantId == null && a.Source == AgentSources.Yaml)));
    }

    /// <summary>
    /// 从属于 Agent 的行（授权 junction、版本快照）的可见集合：本租户的行 ∪ 挂在共享定义上的宿主行。
    /// 共享定义的授权与 Agent 本身同样由 YAML 同步写入、同样 <c>TenantId</c> 为 null，
    /// 只放行 Agent 本身而不放行它的授权，租户解析出来的就是一个没有任何工具的空壳。
    /// </summary>
    /// <remarks>子实体须有名为 <c>Agent</c> 的导航；带软删的在摘掉全局过滤器后手工补回软删条件。</remarks>
    public IQueryable<TChild> ApplyToAgentChildren<TChild>(IQueryable<TChild> rows)
        where TChild : class, IMultiTenant
    {
        Check.NotNull(rows);
        if (!IsTenantCaller)
        {
            return rows;
        }

        var tenantId = CallerTenantId;
        var query = rows.IgnoreQueryFilters();
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TChild)))
        {
            query = query.Where(r => !EF.Property<bool>(r, nameof(ISoftDelete.IsDeleted)));
        }

        return query.Where(r => EF.Property<Guid?>(r, nameof(IMultiTenant.TenantId)) == tenantId
                                || (EF.Property<Guid?>(r, nameof(IMultiTenant.TenantId)) == null
                                    && EF.Property<Agent>(r, AgentNavigation).TenantId == null
                                    && EF.Property<Agent>(r, AgentNavigation).Source == AgentSources.Yaml
                                    && !EF.Property<Agent>(r, AgentNavigation).IsDeleted));
    }

    private const string AgentNavigation = nameof(AgentToolGrant.Agent);
}
