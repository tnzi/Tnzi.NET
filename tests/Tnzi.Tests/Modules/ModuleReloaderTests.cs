using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.Tests.Modules;

/// <summary>
/// ModuleReloader 测试
/// 验证「模块重初始化」语义：按序执行 OnApplicationShutdownAsync → OnApplicationInitializationAsync，
/// 以及 Enabled=false 时不执行重初始化 / 不启动文件监听。
/// </summary>
public class ModuleReloaderTests
{
    [Fact]
    public async Task ReloadModuleAsync_WhenEnabled_RunsShutdownThenInitInOrder()
    {
        var module = new RecordingModule();
        var reloader = CreateReloader(module, enabled: true, out _);

        var ok = await reloader.ReloadModuleAsync(typeof(RecordingModule));

        Assert.True(ok);
        Assert.Equal(new[] { "shutdown", "init" }, module.Calls);
    }

    [Fact]
    public async Task ReloadModuleAsync_WhenDisabled_DoesNotRunLifecycleAndReturnsFalse()
    {
        var module = new RecordingModule();
        var reloader = CreateReloader(module, enabled: false, out _);

        var ok = await reloader.ReloadModuleAsync(typeof(RecordingModule));

        Assert.False(ok);
        Assert.Empty(module.Calls);
    }

    [Fact]
    public async Task StartWatchingAsync_WhenDisabled_DoesNotStartWatching()
    {
        var module = new RecordingModule();
        var reloader = CreateReloader(module, enabled: false, out _);

        await reloader.StartWatchingAsync();

        Assert.False(reloader.IsWatching);
    }

    [Fact]
    public async Task ReloadModuleAsync_WithHotReloadModule_InvokesHooksAroundReinitialization()
    {
        var module = new HotReloadRecordingModule();
        var reloader = CreateReloader(module, enabled: true, out _);

        var ok = await reloader.ReloadModuleAsync(typeof(HotReloadRecordingModule));

        Assert.True(ok);
        // 完整序列：否决前置 → 保存状态 → 关闭 → 初始化 → 恢复状态 → 后置回调
        Assert.Equal(
            new[] { "before", "getState", "shutdown", "init", "restoreState", "after" },
            module.Calls);
    }

    [Fact]
    public async Task ReloadModuleAsync_WhenModuleNotFound_ReturnsFalse()
    {
        var module = new RecordingModule();
        var reloader = CreateReloader(module, enabled: true, out _);

        var ok = await reloader.ReloadModuleAsync(typeof(TestModuleA));

        Assert.False(ok);
        Assert.Empty(module.Calls);
    }

    private static ModuleReloader CreateReloader(ITnziModule module, bool enabled, out Mock<ITnziApplication> app)
    {
        var descriptor = new ModuleDescriptor(module.GetType(), module);
        var provider = new ServiceCollection().BuildServiceProvider();

        // 用 Moq 生成 ITnziApplication 替身，避免在测试源码中命名 ASP.NET Core 类型
        // （InitializeAsync 签名含 IApplicationBuilder/IWebHostEnvironment/WebApplication）。
        app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(new List<IModuleDescriptor> { descriptor });
        app.SetupGet(a => a.ServiceProvider).Returns(provider);

        var options = Microsoft.Extensions.Options.Options.Create(new ModuleHotReloadOptions { Enabled = enabled });
        var watcher = new ModuleFileWatcher(NullLogger<ModuleFileWatcher>.Instance, options);
        return new ModuleReloader(NullLogger<ModuleReloader>.Instance, options, watcher, app.Object);
    }

    private sealed class RecordingModule : TnziCoreModule
    {
        public List<string> Calls { get; } = new();

        public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
        {
            Calls.Add("shutdown");
            return Task.CompletedTask;
        }

        public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
        {
            Calls.Add("init");
            return Task.CompletedTask;
        }
    }

    private sealed class HotReloadRecordingModule : TnziCoreModule, IModuleHotReload
    {
        public List<string> Calls { get; } = new();

        public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
        {
            Calls.Add("shutdown");
            return Task.CompletedTask;
        }

        public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
        {
            Calls.Add("init");
            return Task.CompletedTask;
        }

        public Task<bool> OnBeforeReloadAsync()
        {
            Calls.Add("before");
            return Task.FromResult(true);
        }

        public Task OnAfterReloadAsync()
        {
            Calls.Add("after");
            return Task.CompletedTask;
        }

        public Task<object?> GetStateAsync()
        {
            Calls.Add("getState");
            return Task.FromResult<object?>(null);
        }

        public Task RestoreStateAsync(object? state)
        {
            Calls.Add("restoreState");
            return Task.CompletedTask;
        }
    }
}
