using Microsoft.Extensions.Configuration;
using Tnzi.Hangfire.Options;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 配了队列就整体替换出厂的 <c>["default"]</c>，不是接在它后面。
/// </summary>
/// <remarks>
/// .NET 配置绑定对已有的集合是追加语义。属性初始值预置 <c>["default"]</c> 时，配的 <c>["critical", "default"]</c>
/// 会绑成 <c>["default", "critical", "default"]</c>：Hangfire 按列表顺序取队列，<c>default</c> 被排到 <c>critical</c> 前面，
/// 优先级整个倒过来；只配 <c>["reports"]</c> 的专用服务器也照样去拉 <c>default</c> 队列的任务。
/// 用例经真实绑定（与模块同一个 <c>Get&lt;HangfireOptions&gt;()</c>），不经 <c>new HangfireOptions()</c>。
/// </remarks>
public class HangfireQueueOptionsTests
{
    private static HangfireOptions Bind(params string[] queues)
    {
        var settings = queues
            .Select((queue, index) => new KeyValuePair<string, string?>($"Hangfire:Server:Queues:{index}", queue));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new HangfireOptions();
    }

    [Fact]
    public void ConfiguredQueues_ReplaceTheDefault_InTheirOrder()
        => Assert.Equal(["critical", "default"], Bind("critical", "default").Server.ResolveQueues());

    [Fact]
    public void ADedicatedServer_DoesNotAlsoPullTheDefaultQueue()
        => Assert.Equal(["reports"], Bind("reports").Server.ResolveQueues());

    [Fact]
    public void UnconfiguredQueues_AreTheDefaultQueue()
        => Assert.Equal(["default"], Bind().Server.ResolveQueues());

    [Fact]
    public void DuplicateQueues_AreListedOnce()
        => Assert.Equal(["critical", "default"], Bind("critical", "critical", "default").Server.ResolveQueues());
}
