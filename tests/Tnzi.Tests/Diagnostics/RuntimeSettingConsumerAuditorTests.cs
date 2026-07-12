using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tnzi.Modules.Diagnostics;
using Tnzi.Settings;
using Xunit;

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
}
