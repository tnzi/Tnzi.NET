namespace Tnzi.EFCore;

/// <summary>
/// 随 <see cref="DbContextOptions"/> 一起到达 DbContext 的多租户开关。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>存在的理由：开关必须经一条消费方构造函数不参与的路到达 DbContext。</b>
/// 两个 DbContext 基类的开关原本只取自<b>可选</b>构造参数 <c>IOptions&lt;MultiTenancyOptions&gt;</c>，
/// 而脚手架模板、文档示例与消费方的 DbContext 一律只声明 <c>(DbContextOptions, ICurrentUser)</c>。
/// DI 只能填声明过的形参，所以运行期那个参数<b>永远是 null</b>：
/// <c>MultiTenancy:Enabled = true</c> 配置生效、日志正常、接口 200，而 <c>TenantId</c> 不落库、
/// 租户过滤器一条都不加 —— 多租户整条从未生效。设计期工厂同样传不进那个参数。
/// </para>
/// <para>
/// <see cref="DbContextOptions"/> 是运行期（<c>AddTnziDbContext</c> 的 options 回调）与设计期
/// （<see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 的 optionsBuilder）都会经过、
/// 且构造函数必然接收的唯一载体。开关放在这里，两侧从同一份配置读出同一个值，
/// 消费方的构造函数一字不改；手写设计期工厂的消费方调 <see cref="MultiTenancyOptionsBuilderExtensions.UseTnziMultiTenancy"/>。
/// </para>
/// <para>
/// 开关参与内部服务提供者的哈希：单/多租户两套模型缓存在不同的内部容器里，
/// 不靠 <see cref="Internal.MultiTenancyModelCacheKeyFactory"/> 也不会串用（那个工厂只在
/// <c>AddTnziDbContext</c> 注册的上下文上生效，裸 <c>AddDbContext</c> 或直接 new 的上下文拿不到它）。
/// </para>
/// </remarks>
public sealed class MultiTenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public MultiTenancyOptionsExtension(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>多租户是否启用。</summary>
    public bool Enabled { get; }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        // 开关本身不向内部容器登记任何服务；它只是 options 上的一个值。
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(MultiTenancyOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new MultiTenancyOptionsExtension Extension => (MultiTenancyOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => Extension.Enabled ? "MultiTenancy=on " : "MultiTenancy=off ";

        public override int GetServiceProviderHashCode() => Extension.Enabled.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo && otherInfo.Extension.Enabled == Extension.Enabled;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Tnzi:MultiTenancy"] = Extension.Enabled.ToString();
        }
    }
}
