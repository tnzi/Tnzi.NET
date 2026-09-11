using Tnzi.Modules.Diagnostics;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：框架自身不得留下 <c>RuntimeSettingConsumerAuditor</c> 的高置信告警。
/// </summary>
/// <remarks>
/// <para>
/// 这条审计是给<b>消费应用</b>用的 —— 它在每个应用的启动日志里说话。框架自己留着告警有两笔代价：
/// 一是消费方分不清哪条是自己的问题，二是天天见到的告警会被训练成背景噪音，
/// 于是下一条真告警（admin 改了设置不生效，且不报错、无症状）也一起被忽略。
/// </para>
/// <para>
/// <b>覆盖面是下界不是全集。</b>两处天然盲区：①按配置门控的注册（<c>AI:Channels:Enabled</c>
/// 这类，默认关闭时整批服务根本不进服务图）在本夹具里看不到；②工厂 lambda 注册的服务没有
/// <c>ImplementationType</c>，审计本身就看不见构造参数。所以这道门禁绿不等于消费方启动零告警，
/// 它保证的是「默认组合下的框架服务是干净的」。
/// </para>
/// <para>
/// 只断言 <c>DirectWarnings</c>：嵌套提示是低置信信息（审计无法确定消费者读没读到热字段），
/// 按 Information 输出，逐条核实属于治理工作而不是门禁职责 —— 把它锁进断言只会制造
/// 「为了让门禁绿而随手加声明」的压力，而一份随手写的 <c>[ReadsOnlyColdSettings]</c>
/// 恰好是这条审计最怕的东西。
/// </para>
/// </remarks>
public class RuntimeSettingConsumerAuditTests
{
    [Fact]
    public void FrameworkServices_ShouldNotConsumeHotSettingsViaIOptions()
    {
        var result = ArchitectureModuleGraph.Load();

        if (result.Failures.Count > 0)
        {
            var failures = string.Join(Environment.NewLine, result.Failures.Select(f => $"  - {f}"));
            Assert.Fail(
                $"{result.Failures.Count} module(s) failed to configure - their services would silently drop "
                + $"out of this audit:{Environment.NewLine}{failures}");
        }

        var audit = RuntimeSettingConsumerAuditor.AuditDetailed(
            result.ServiceMap.SelectMany(kv => kv.Value),
            result.ServiceMap.Keys.Select(t => t.Assembly).Distinct());

        if (audit.DirectWarnings.Count > 0)
        {
            var report = string.Join(Environment.NewLine, audit.DirectWarnings.Select(w => $"  - {w}"));
            Assert.Fail(
                $"{audit.DirectWarnings.Count} framework service(s) consume hot-settable options via "
                + $"IOptions<T>. Every consuming application sees these in its startup log:"
                + $"{Environment.NewLine}{report}");
        }
    }
}
