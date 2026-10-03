using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 框架对「必需导航指向带查询过滤器的主体」这条 EF 模型警告的立场
/// （<see cref="ModelWarningOptionsBuilderExtensions.UseTnziModelWarningPolicy(DbContextOptionsBuilder)"/>）：
/// 默认压掉，消费方显式表态则不动，且 <c>AddTnziDbContext</c> 自动带上它。
/// </summary>
/// <remarks>
/// <para>
/// 软删过滤器挂在每个 <c>ISoftDelete</c> 实体上，而令牌 / 会话 / 密码历史 / 登录策略这类从属行对主体
/// 都是必需外键 —— 这条警告在每个消费方启动时打八遍以上，真正的警告淹没在里面。框架的语义是
/// 「从属行不加匹配过滤器，经导航到达已软删主体时随主体一起出视野」，理由见扩展方法的备注。
/// </para>
/// <para>
/// ★ 每个用例用<b>自己的上下文类型</b>：EF 按上下文类型缓存模型，模型校验（警告就在那一步发出）
/// 只跑一次；共用一个类型的话后面的用例会拿到先建好的模型，什么都不会记录，无论立场对不对。
/// 「消费方显式 Log」那条同时是正控制：它证明这个模型确实会触发这条警告，第一条的沉默不是因为没触发。
/// </para>
/// <para>
/// ★ 立场写在 options 上而不是 <c>OnConfiguring</c> 里：警告配置参与 EF 内部服务提供者的缓存键，
/// 而 <c>DbContext</c> 构造函数按构造时的 options 先取一次提供者、<c>OnConfiguring</c> 改过之后再取一次 ——
/// 每种 options 形状两个提供者。本测试项目跑到第 21 个就会撞 <c>ManyServiceProvidersCreatedWarning</c>
/// 的抛出（实发过），消费方在生产里则是每个上下文白付一套 EF 单例。
/// </para>
/// </remarks>
public class RequiredNavigationWarningPolicyTests
{
    private static readonly EventId Warning = CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning;

    [Fact]
    public void WithThePolicy_RequiredNavigationToASoftDeletedPrincipal_DoesNotLogTheWarning()
    {
        var logged = new List<string>();
        var options = new DbContextOptionsBuilder<SilencedProbeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .LogTo(logged.Add, [Warning])
            .UseTnziModelWarningPolicy()
            .Options;

        using var context = new SilencedProbeDbContext(options, new MockCurrentUser());
        _ = context.Model;

        Assert.Empty(logged);
    }

    [Fact]
    public void WhenTheConsumerExplicitlyAsksForIt_TheWarningStillLogs()
    {
        var logged = new List<string>();
        var options = new DbContextOptionsBuilder<OptedInProbeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .ConfigureWarnings(warnings => warnings.Log(Warning))
            .LogTo(logged.Add, [Warning])
            .UseTnziModelWarningPolicy()
            .Options;

        using var context = new OptedInProbeDbContext(options, new MockCurrentUser());
        _ = context.Model;

        var entry = Assert.Single(logged);
        Assert.Contains(nameof(ProbePrincipal), entry);
        Assert.Contains(nameof(ProbeDependent), entry);
    }

    [Fact]
    public void WhenTheConsumerThrowsOnIt_ThePolicyDoesNotOverrideThat()
    {
        var options = new DbContextOptionsBuilder<ThrowingProbeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .ConfigureWarnings(warnings => warnings.Throw(Warning))
            .UseTnziModelWarningPolicy()
            .Options;

        using var context = new ThrowingProbeDbContext(options, new MockCurrentUser());

        var ex = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains(nameof(ProbeDependent), ex.Message);
    }

    /// <summary>消费方走的是 AddTnziDbContext，不会自己调扩展 —— 漏斗必须带上它。</summary>
    [Fact]
    public void AddTnziDbContext_AppliesThePolicy()
    {
        var logged = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddTnziDbContext<FunnelProbeDbContext>(options => options
            .UseSqlite("Data Source=:memory:")
            .LogTo(logged.Add, [Warning]));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FunnelProbeDbContext>();
        _ = context.Model;

        Assert.Empty(logged);
    }
}

/// <summary>软删主体：框架给它挂全局过滤器。</summary>
public class ProbePrincipal : FullAuditedEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>对主体是必需外键的从属行，自己不软删 —— 令牌 / 会话 / 登录策略的形状。</summary>
public class ProbeDependent : EntityBase<Guid>
{
    public Guid PrincipalId { get; set; }

    public virtual ProbePrincipal Principal { get; set; } = null!;
}

public abstract class RequiredNavigationProbeDbContext<TSelf> : TnziDbContext<TSelf>
    where TSelf : RequiredNavigationProbeDbContext<TSelf>
{
    protected RequiredNavigationProbeDbContext(DbContextOptions<TSelf> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<ProbePrincipal> Principals => Set<ProbePrincipal>();

    public DbSet<ProbeDependent> Dependents => Set<ProbeDependent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProbeDependent>()
            .HasOne(d => d.Principal)
            .WithMany()
            .HasForeignKey(d => d.PrincipalId)
            .IsRequired();
    }
}

public class SilencedProbeDbContext(DbContextOptions<SilencedProbeDbContext> options, ICurrentUser currentUser)
    : RequiredNavigationProbeDbContext<SilencedProbeDbContext>(options, currentUser);

public class OptedInProbeDbContext(DbContextOptions<OptedInProbeDbContext> options, ICurrentUser currentUser)
    : RequiredNavigationProbeDbContext<OptedInProbeDbContext>(options, currentUser);

public class ThrowingProbeDbContext(DbContextOptions<ThrowingProbeDbContext> options, ICurrentUser currentUser)
    : RequiredNavigationProbeDbContext<ThrowingProbeDbContext>(options, currentUser);

public class FunnelProbeDbContext(DbContextOptions<FunnelProbeDbContext> options, ICurrentUser currentUser)
    : RequiredNavigationProbeDbContext<FunnelProbeDbContext>(options, currentUser);
