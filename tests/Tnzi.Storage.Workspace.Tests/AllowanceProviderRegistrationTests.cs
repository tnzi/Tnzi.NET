using Tnzi.Modules;

namespace Tnzi.Storage.Workspace.Tests;

/// <summary>
/// 配额来源的注册：加载了本模块时生效的必须是覆盖表那一份，只加载父模块时是只读默认值的那一份。
/// </summary>
/// <remarks>
/// ★ 这里守的是一个不会让任何业务测试变红的陷阱：父模块的回退若在配置阶段 <c>TryAdd</c>（LoadOrder 更低，先注册），
/// 本模块随后的 <c>TryAdd</c> 就被静默挡掉 —— 配额照常工作、只读默认值，管理端设的按用户覆盖值从此没人读。
/// 按生命周期顺序真的跑一遍两个模块的注册，再看容器最终解析出哪一个实现。
/// </remarks>
public class AllowanceProviderRegistrationTests
{
    private static async Task<ServiceProvider> BuildAsync(params ITnziModule[] modules)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var context = new ServiceConfigurationContext(services, configuration);

        foreach (var module in modules)
            await module.ConfigureServicesAsync(context);
        foreach (var module in modules)
            await module.PostConfigureServicesAsync(context);

        // 两个实现都只要仓储与选项；这里只看注册解析出哪一个类型，仓储给一个占位。
        services.AddSingleton(new Mock<IRepository<StorageAllowance, Guid>>().Object);
        services.AddSingleton<IOptionsMonitor<StorageOptions>>(new StaticOptionsMonitor<StorageOptions>(new StorageOptions()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WithTheWorkspace_TheOverrideTableIsTheAllowanceSource()
    {
        await using var provider = await BuildAsync(new StorageModule(), new StorageWorkspaceModule());
        using var scope = provider.CreateScope();

        Assert.IsType<StorageAllowanceProvider>(scope.ServiceProvider.GetRequiredService<IStorageAllowanceProvider>());
    }

    [Fact]
    public async Task WithoutTheWorkspace_TheDefaultOnlySourceIsRegistered()
    {
        await using var provider = await BuildAsync(new StorageModule());
        using var scope = provider.CreateScope();

        Assert.IsType<OptionsStorageAllowanceProvider>(scope.ServiceProvider.GetRequiredService<IStorageAllowanceProvider>());
    }
}
