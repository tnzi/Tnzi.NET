namespace Tnzi.EFCore.Internal;

/// <summary>
/// 两个 DbContext 基类共用的多租户开关解析。
/// </summary>
/// <remarks>
/// <para>
/// 优先级：消费方显式转发的 <c>IOptions&lt;MultiTenancyOptions&gt;</c> &gt;
/// <see cref="DbContextOptions"/> 里的 <see cref="MultiTenancyOptionsExtension"/> &gt; <c>false</c>。
/// </para>
/// <para>
/// ★ 第一项在绝大多数上下文里是 <c>null</c>：脚手架模板、文档示例与消费方的 DbContext 只声明
/// <c>(DbContextOptions, ICurrentUser)</c>，DI 填不了没声明的形参。它存在只为兼容
/// 确实转发了该选项的上下文（如框架自带的 <c>RagDbContext</c>）；运行期两者来自同一份配置，
/// 答案必然一致，<c>EFCoreModule</c> 启动时会核对这一点。
/// </para>
/// <para>
/// 第二项才是运行期与设计期共同走的那条路：<c>AddTnziDbContext</c> 与
/// <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 各自从同一份配置读出开关放进 options。
/// 此前这里曾是一个由设计期工厂写入的进程级静态，并注释称「运行期永远读不到这里」——
/// 那句话是错的：运行期恰恰每次都读到这里，而没有任何东西往里写，于是恒 <c>false</c>。
/// </para>
/// <para>
/// 两个基类共用这一处，因为它们的开关必须给出同一个答案 ——
/// 两份各自的三元表达式漂开时不会有任何东西报错，只是其中一个基类的消费方拿到错的索引。
/// </para>
/// </remarks>
public static class MultiTenancySwitch
{
    public static bool Resolve(bool? injected, IDbContextOptions options)
    {
        Check.NotNull(options);
        return injected ?? options.FindExtension<MultiTenancyOptionsExtension>()?.Enabled ?? false;
    }
}
