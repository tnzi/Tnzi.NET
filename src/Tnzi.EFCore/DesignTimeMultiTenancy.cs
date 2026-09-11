namespace Tnzi.EFCore;

/// <summary>
/// 设计期（<c>dotnet ef</c>）的多租户开关。<b>只在没有 DI 的那条路径上使用。</b>
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>存在的理由是一条会让多租户迁移全部生成错的路径。</b>
/// <see cref="TnziDbContext{TDbContext}"/>（以及 <c>IdentityDbContext</c>）的多租户开关取自
/// <b>可选</b>构造参数 <c>IOptions&lt;MultiTenancyOptions&gt;</c>，缺省即 <c>false</c>；
/// 而 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 只反射
/// <c>(DbContextOptions, ICurrentUser)</c> 与 <c>(DbContextOptions)</c> 两种构造函数，
/// <b>从来不传那个参数</b> —— 消费方自己的 DbContext 通常也没有那个形参可传。
/// </para>
/// <para>
/// 后果不是一条烦人的警告，而是<b>迁移内容本身是错的</b>：全框架有一百二十多个实体配置
/// 按这个开关分支，而分的往往不是过滤条件而是<b>索引的列集</b>
/// （例如「每往来方至多一个默认账户」在多租户下是 <c>(TenantId, PartyType, PartyId)</c>、
/// 单租户下是 <c>(PartyType, PartyId)</c>）。设计期恒 <c>false</c> 意味着：
/// </para>
/// <list type="number">
/// <item>生成的迁移建的是<b>不含 TenantId</b> 的唯一索引，于是那条跨租户不变量<b>从未进过数据库</b>；</item>
/// <item>运行期模型与快照永久不一致，EF 10 的 <c>database update</c> 守卫据此拒绝执行 ——
/// 而<b>再加多少条迁移都消不掉</b>，因为每一条都由同一份设计期视图生成。</item>
/// </list>
/// <para>
/// ★ 修法刻意<b>不</b>要求消费方给 DbContext 加一个三参构造函数：那对没改的人症状一字不变，
/// 而症状恰恰是无声的。改为由设计期工厂从它<b>已经加载过</b>的 <c>appsettings.json</c> 里读出
/// <c>MultiTenancy:Enabled</c> 放在这里，两个 DbContext 基类在注入值缺席时读它。
/// 既有消费方一个字都不用改。
/// </para>
/// <para>
/// ★ 运行期<b>永远读不到这里</b>：DI 一定会注入 <c>IOptions&lt;MultiTenancyOptions&gt;</c>，
/// 注入值优先。这个静态值只在「没有容器」的设计期有意义，所以它是可变静态也不构成运行期风险。
/// </para>
/// </remarks>
public static class DesignTimeMultiTenancy
{
    /// <summary>
    /// 设计期读到的开关；<c>null</c> 表示不在设计期（或工厂没能读到配置），此时回退 <c>false</c>。
    /// </summary>
    public static bool? Enabled { get; set; }

    /// <summary>
    /// 解析多租户开关：注入值优先，其次设计期值，最后 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 两个 DbContext 基类共用这一处，因为它们的开关必须给出同一个答案 ——
    /// 两份各自的三元表达式漂开时不会有任何东西报错，只是其中一个基类的消费方拿到错的索引。
    /// </remarks>
    public static bool Resolve(bool? injected) => injected ?? Enabled ?? false;
}
