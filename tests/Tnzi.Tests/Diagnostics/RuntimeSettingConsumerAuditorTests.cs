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
}
