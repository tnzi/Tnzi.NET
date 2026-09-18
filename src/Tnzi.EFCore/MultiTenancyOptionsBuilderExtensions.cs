namespace Tnzi.EFCore;

/// <summary>
/// 把多租户开关写进 <see cref="DbContextOptionsBuilder"/>。
/// </summary>
public static class MultiTenancyOptionsBuilderExtensions
{
    /// <summary>
    /// 声明该 DbContext 的多租户开关。<c>AddTnziDbContext</c> 与
    /// <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 都会自动调用；
    /// 只有<b>不用</b>框架工厂基类、自己实现 <c>IDesignTimeDbContextFactory</c> 的消费方需要手动调用它，
    /// 否则那条路上建出的模型恒为单租户，生成的迁移与运行期模型分叉。
    /// </summary>
    public static DbContextOptionsBuilder UseTnziMultiTenancy(this DbContextOptionsBuilder optionsBuilder, bool enabled)
    {
        Check.NotNull(optionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new MultiTenancyOptionsExtension(enabled));
        return optionsBuilder;
    }

    /// <inheritdoc cref="UseTnziMultiTenancy(DbContextOptionsBuilder, bool)"/>
    public static DbContextOptionsBuilder<TDbContext> UseTnziMultiTenancy<TDbContext>(this DbContextOptionsBuilder<TDbContext> optionsBuilder, bool enabled)
        where TDbContext : DbContext
        => (DbContextOptionsBuilder<TDbContext>)UseTnziMultiTenancy((DbContextOptionsBuilder)optionsBuilder, enabled);
}
