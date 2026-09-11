using Tnzi.Settings;

namespace Tnzi.Tests.Diagnostics;

public class RuntimeSettingConsumerAuditorTests
{
    [ConfigSection("Demo")]
    public sealed class HotOptions { [RuntimeSetting] public string Name { get; set; } = ""; }
    public sealed class BadConsumer { public BadConsumer(IOptions<HotOptions> o) { _ = o; } }
    public sealed class GoodConsumer { public GoodConsumer(IOptionsMonitor<HotOptions> o) { _ = o; } }
    public sealed class SnapshotConsumer { public SnapshotConsumer(IOptionsSnapshot<HotOptions> o) { _ = o; } }

    /// <summary>聚合 options：自身无 [RuntimeSetting]，但嵌套属性类型带热字段。</summary>
    [ConfigSection("Aggregate")]
    public sealed class AggregateOptions { public HotOptions Nested { get; set; } = new(); }
    public sealed class AggregateConsumer { public AggregateConsumer(IOptions<AggregateOptions> o) { _ = o; } }

    [ConfigSection("Cold")]
    public sealed class ColdOptions { public string Name { get; set; } = ""; }
    public sealed class ColdConsumer { public ColdConsumer(IOptions<ColdOptions> o) { _ = o; } }

    /// <summary>冷热同居一个 options 类：部署机密（冷）与可热改字段（热）的真实形态。</summary>
    [ConfigSection("Mixed")]
    public sealed class MixedOptions
    {
        public string? SigningKey { get; set; }
        [RuntimeSetting] public int TtlSeconds { get; set; }
    }

    public sealed class DeclaredColdConsumer
    {
        public DeclaredColdConsumer(
            [ReadsOnlyColdSettings(nameof(MixedOptions.SigningKey))] IOptions<MixedOptions> o) { _ = o; }
    }

    public sealed class DeclaresHotFieldConsumer
    {
        public DeclaresHotFieldConsumer(
            [ReadsOnlyColdSettings(nameof(MixedOptions.TtlSeconds))] IOptions<MixedOptions> o) { _ = o; }
    }

    public sealed class DeclaresUnknownFieldConsumer
    {
        public DeclaresUnknownFieldConsumer(
            [ReadsOnlyColdSettings("RenamedAwayLongAgo")] IOptions<MixedOptions> o) { _ = o; }
    }

    public sealed class DeclaresNothingConsumer
    {
        public DeclaresNothingConsumer([ReadsOnlyColdSettings] IOptions<MixedOptions> o) { _ = o; }
    }

    [ConfigSection("NestedMixed")]
    public sealed class NestedMixedOptions { public MixedOptions Inner { get; set; } = new(); }

    public sealed class DeclaredColdNestedConsumer
    {
        public DeclaredColdNestedConsumer(
            [ReadsOnlyColdSettings("Inner.SigningKey")] IOptions<NestedMixedOptions> o) { _ = o; }
    }

    public sealed class DeclaresHotNestedConsumer
    {
        public DeclaresHotNestedConsumer(
            [ReadsOnlyColdSettings("Inner.TtlSeconds")] IOptions<NestedMixedOptions> o) { _ = o; }
    }

    private static readonly Assembly Asm = typeof(RuntimeSettingConsumerAuditorTests).Assembly;

    [Fact]
    public void Warns_when_runtime_setting_consumed_via_IOptions()
    {
        var d = new[] { ServiceDescriptor.Scoped<BadConsumer, BadConsumer>() };
        var w = RuntimeSettingConsumerAuditor.AuditAndReport(d, new[] { Asm });
        Assert.Single(w);
        Assert.Contains("HotOptions", w[0]);
    }

    [Fact]
    public void No_warning_for_IOptionsMonitor()
    {
        var d = new[] { ServiceDescriptor.Scoped<GoodConsumer, GoodConsumer>() };
        Assert.Empty(RuntimeSettingConsumerAuditor.AuditAndReport(d, new[] { Asm }));
    }

    [Fact]
    public void No_warning_for_IOptionsSnapshot()
    {
        // Snapshot 是 Scoped 服务：能被注入即每请求重算（= 热）。Singleton 注入
        // Snapshot 会被 DI 作用域校验直接拒绝，不属于本审计的职责。
        var d = new[] { ServiceDescriptor.Scoped<SnapshotConsumer, SnapshotConsumer>() };
        Assert.Empty(RuntimeSettingConsumerAuditor.AuditAndReport(d, new[] { Asm }));
    }

    [Fact]
    public void Nested_aggregate_via_IOptions_produces_low_confidence_hint()
    {
        var d = new[] { ServiceDescriptor.Scoped<AggregateConsumer, AggregateConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        Assert.Empty(result.DirectWarnings);
        Assert.Single(result.NestedHints);
        Assert.Contains("AggregateOptions", result.NestedHints[0]);
    }

    [Fact]
    public void Cold_options_via_IOptions_is_clean()
    {
        var d = new[] { ServiceDescriptor.Scoped<ColdConsumer, ColdConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        Assert.Empty(result.DirectWarnings);
        Assert.Empty(result.NestedHints);
    }

    /// <summary>
    /// 判据是「消费者读没读到热字段」，不是「这个 options 类里有没有热字段」。
    /// 只读冷字段并如实声明的消费者不该被告警 —— 否则每个「机密与热设置同住一个 options 类」
    /// 的服务都在刷噪音，而噪音正是让真告警被忽略的原因。
    /// </summary>
    [Fact]
    public void Declared_cold_only_read_is_not_warned()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaredColdConsumer, DeclaredColdConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        Assert.Empty(result.DirectWarnings);
        Assert.Empty(result.NestedHints);
    }

    /// <summary>声明里混进热字段 = 这条声明本身就是缺陷，必须比不声明更响。</summary>
    [Fact]
    public void Declaration_naming_a_hot_field_is_warned()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaresHotFieldConsumer, DeclaresHotFieldConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        var warning = Assert.Single(result.DirectWarnings);
        Assert.Contains(nameof(MixedOptions.TtlSeconds), warning);
        Assert.Contains("[RuntimeSetting]", warning);
    }

    /// <summary>属性改名后声明失配：报「没有这个属性」而不是静默放行。</summary>
    [Fact]
    public void Declaration_naming_a_missing_property_is_warned()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaresUnknownFieldConsumer, DeclaresUnknownFieldConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        var warning = Assert.Single(result.DirectWarnings);
        Assert.Contains("RenamedAwayLongAgo", warning);
    }

    /// <summary>空声明不是「我审过了」，是「我什么都没说」—— 当成把审计关掉处理。</summary>
    [Fact]
    public void Empty_declaration_is_warned()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaresNothingConsumer, DeclaresNothingConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        var warning = Assert.Single(result.DirectWarnings);
        Assert.Contains("lists no property", warning);
    }

    /// <summary>嵌套聚合同一套判据：点号路径解析到的字段是冷的就放行。</summary>
    [Fact]
    public void Declared_cold_nested_path_is_not_hinted()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaredColdNestedConsumer, DeclaredColdNestedConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        Assert.Empty(result.DirectWarnings);
        Assert.Empty(result.NestedHints);
    }

    /// <summary>
    /// 嵌套路径指向热字段：升级成高置信告警，不是留在低置信提示里。
    /// 命中级别由「声明是否成立」决定，不由「命中是直接还是嵌套」决定。
    /// </summary>
    [Fact]
    public void Declaration_naming_a_hot_nested_field_is_warned_not_hinted()
    {
        var d = new[] { ServiceDescriptor.Singleton<DeclaresHotNestedConsumer, DeclaresHotNestedConsumer>() };
        var result = RuntimeSettingConsumerAuditor.AuditDetailed(d, new[] { Asm });
        Assert.Empty(result.NestedHints);
        var warning = Assert.Single(result.DirectWarnings);
        Assert.Contains("Inner.TtlSeconds", warning);
    }
}
