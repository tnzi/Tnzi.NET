namespace Tnzi.EFCore;

/// <summary>
/// 把框架对 EF Core 模型警告的立场写进 <see cref="DbContextOptionsBuilder"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>「必需导航指向带查询过滤器的主体」这条警告
/// （<see cref="CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning"/>）
/// 在本框架里是结构性的，不是某个实体配错了。</b>软删过滤器挂在每一个 <c>ISoftDelete</c> 实体上
/// （<c>User</c>、<c>FullAuditedEntity</c> 全族），而令牌 / 会话 / 密码历史 / 登录策略 / 会话成员 /
/// 通知收件人这类从属行对主体都是必需外键 —— 每个消费方启动时同一条警告打八遍以上，
/// 真正的警告淹没在里面。
/// </para>
/// <para>
/// 框架对这种关系的语义已定：<b>从属实体不加匹配过滤器；经导航到达已软删主体时，从属行随主体一起出视野</b>
/// （<c>Include</c> 与经导航的谓词走 INNER JOIN，已软删主体的从属行不出现），而不经导航的查询照常看得到它们 ——
/// 会话清扫、令牌撤销、审计回溯都要读到已删账号的从属行。EF 建议的两条修法都改行为：
/// 给从属实体加匹配过滤器会让这些行对维护任务消失，并给每次令牌 / 会话查找多一个到主体表的 JOIN；
/// 把导航改可选是外键列可空性变更，要迁移。
/// </para>
/// <para>
/// ★ 只在消费方没有明确表态时压掉：<c>ConfigureWarnings</c> 里对这个事件显式给过
/// <c>Log</c> / <c>Throw</c> / <c>Ignore</c> 的，一律不改 —— 想把它当错误的部署仍然可以。
/// 所以本方法必须在消费方自己的 options 配置<b>之后</b>调用。
/// </para>
/// <para>
/// ★ <b>写在 options 上而不是 <c>OnConfiguring</c> 里</b>：警告配置参与 EF 内部服务提供者的缓存键，
/// 而 <c>DbContext</c> 的构造函数会先按构造时的 options 取一次内部提供者（初始化 DbSet），
/// <c>OnConfiguring</c> 改过 options 之后又会按改过的 options 再建一个 —— 每种 options 形状两个提供者，
/// 正是 EF 的 <c>ManyServiceProvidersCreatedWarning</c> 点名的形态。<c>AddTnziDbContext</c> 与
/// <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 都会自动调用；自己 <c>new DbContextOptionsBuilder</c>
/// 的消费方（自建测试底座、不用框架工厂基类的设计期工厂）按需手动调用，不调用只是警告照旧打出来。
/// </para>
/// </remarks>
public static class ModelWarningOptionsBuilderExtensions
{
    /// <summary>
    /// 应用框架对 EF Core 模型警告的立场：必需导航指向软删主体的警告默认 <c>Ignore</c>，
    /// 消费方对该事件显式配置过的行为保持不变。
    /// </summary>
    public static DbContextOptionsBuilder UseTnziModelWarningPolicy(this DbContextOptionsBuilder optionsBuilder)
    {
        Check.NotNull(optionsBuilder);

        var warning = CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning;
        var explicitBehavior = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
            ?.WarningsConfiguration.GetBehavior(warning);
        if (explicitBehavior != null)
        {
            return optionsBuilder;
        }

        return optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(warning));
    }

    /// <inheritdoc cref="UseTnziModelWarningPolicy(DbContextOptionsBuilder)"/>
    public static DbContextOptionsBuilder<TDbContext> UseTnziModelWarningPolicy<TDbContext>(this DbContextOptionsBuilder<TDbContext> optionsBuilder)
        where TDbContext : DbContext
        => (DbContextOptionsBuilder<TDbContext>)UseTnziModelWarningPolicy((DbContextOptionsBuilder)optionsBuilder);
}
