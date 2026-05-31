using Microsoft.Extensions.Configuration;

namespace Tnzi.Tests.Modules;

/// <summary>
/// TnziApplication 关闭生命周期测试。
/// 这些不变量现在是承重的：TnziShutdownHostedService.StopAsync 与 Dispose 兜底路径都可能触发
/// ShutdownAsync，因此它必须 (1) 幂等、(2) 按加载逆序执行模块关闭。
/// </summary>
public class TnziApplicationShutdownTests
{
    [Fact]
    public async Task ShutdownAsync_IsIdempotent_AndRunsModulesInReverseLoadOrder()
    {
        ShutdownRecorder.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        var app = new TnziApplication(typeof(RecordingStartupModule), services, configuration);
        await app.ConfigureServicesAsync();

        var serviceProvider = services.BuildServiceProvider();
        await app.InitializeAsync(serviceProvider);

        // 触发两次：第二次必须是 no-op（幂等）
        await app.ShutdownAsync();
        await app.ShutdownAsync();

        // 加载顺序 = [Dependency, Startup]（依赖先加载）⇒ 关闭逆序 = [Startup, Dependency]
        // 幂等 ⇒ 每个模块恰好关闭一次（不重复）
        Assert.Equal(new[] { "Startup", "Dependency" }, ShutdownRecorder.Order.ToArray());
    }
}

internal static class ShutdownRecorder
{
    private static readonly object Gate = new();
    private static readonly List<string> _order = new();

    public static IReadOnlyList<string> Order
    {
        get { lock (Gate) { return _order.ToList(); } }
    }

    public static void Reset()
    {
        lock (Gate) { _order.Clear(); }
    }

    public static void Record(string name)
    {
        lock (Gate) { _order.Add(name); }
    }
}

[DependsOn(typeof(RecordingDependencyModule))]
public class RecordingStartupModule : TnziCustomModule
{
    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        ShutdownRecorder.Record("Startup");
        return Task.CompletedTask;
    }
}

public class RecordingDependencyModule : TnziCustomModule
{
    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        ShutdownRecorder.Record("Dependency");
        return Task.CompletedTask;
    }
}
