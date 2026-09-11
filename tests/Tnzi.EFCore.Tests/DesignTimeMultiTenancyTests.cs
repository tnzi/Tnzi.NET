namespace Tnzi.EFCore.Tests;

/// <summary>
/// 设计期多租户开关的解析。
/// </summary>
/// <remarks>
/// ★★★ 这组守的是一条**内容错误**而不是一条警告：`dotnet ef migrations add` 建模时，
/// DbContext 拿不到 <c>IOptions&lt;MultiTenancyOptions&gt;</c>（工厂只反射两种构造函数，
/// 都不含那个参数，消费方自己的 DbContext 通常也没有那个形参），于是开关恒 <c>false</c>。
/// 而全框架一百二十多个实体配置按它分支，分的往往是**索引的列集** —— 多租户应用因此生成出
/// 不含 <c>TenantId</c> 的唯一索引，跨租户不变量从未进过数据库，同时模型与快照永久不一致。
/// </remarks>
public class DesignTimeMultiTenancyTests : IDisposable
{
    private readonly bool? _original = DesignTimeMultiTenancy.Enabled;

    public void Dispose() => DesignTimeMultiTenancy.Enabled = _original;

    /// <summary>运行期一定有 DI，注入值必须压过设计期值 —— 否则一个忘了清的静态会改变生产行为。</summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    public void InjectedValue_AlwaysWins(bool injected, bool? designTime, bool expected)
    {
        DesignTimeMultiTenancy.Enabled = designTime;

        Assert.Equal(expected, DesignTimeMultiTenancy.Resolve(injected));
    }

    /// <summary>没有注入值（设计期）时才读它。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WithoutInjectedValue_TheDesignTimeSwitchIsUsed(bool designTime, bool expected)
    {
        DesignTimeMultiTenancy.Enabled = designTime;

        Assert.Equal(expected, DesignTimeMultiTenancy.Resolve(null));
    }

    /// <summary>两者都没有 = 不在设计期且没有配置 ⇒ 单租户，与改动前的行为逐字相同。</summary>
    [Fact]
    public void WithNeither_ItFallsBackToSingleTenant()
    {
        DesignTimeMultiTenancy.Enabled = null;

        Assert.False(DesignTimeMultiTenancy.Resolve(null));
    }

    /// <summary>
    /// 存在性证明：DbContext 在<b>没有</b>注入选项时确实读到了设计期开关。
    /// </summary>
    /// <remarks>
    /// 这条是本组的核心。构造 DbContext 时**刻意不传** <c>multiTenancyOptions</c>，
    /// 正是设计期工厂走的那条路；改动前它恒为 false，与设计期值无关。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADbContextBuiltWithoutOptions_ReadsTheDesignTimeSwitch(bool designTime)
    {
        DesignTimeMultiTenancy.Enabled = designTime;

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>()
            .UseSqlite(connection)
            .Options;

        // 与 DesignTimeDbContextFactoryBase.CreateDbContextInstance 同一个构造形状。
        using var context = new MultiTenancyProbeDbContext(options, new DesignTimeCurrentUser());

        Assert.Equal(designTime, context.IsMultiTenancyEnabled);
    }
}

/// <summary>只为上面那条存在性证明而设的最小上下文，构造形状与消费方 DbContext 一致。</summary>
public class MultiTenancyProbeDbContext : TnziDbContext<MultiTenancyProbeDbContext>
{
    public MultiTenancyProbeDbContext(
        DbContextOptions<MultiTenancyProbeDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }
}
