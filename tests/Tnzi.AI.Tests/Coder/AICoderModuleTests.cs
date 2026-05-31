using Tnzi.AI.Coder.Options;
using Tnzi.AI.Coder;
using Tnzi.AI.Coder.ProjectContext;
using Tnzi.AI.ProjectContext;
using Tnzi.AI.Memory;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// AICoderModule 单元测试 — 模块注册（TryAdd 行为验证）
/// </summary>
public class AICoderModuleTests
{
    #region 服务注册

    [Fact]
    public void ConfigureServicesAsync_RegistersPathValidator()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPathValidator));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PathValidator));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ConfigureServicesAsync_RegistersCommandSanitizer()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandSanitizer));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(CommandSanitizer));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ConfigureServicesAsync_RegistersShellAdapterAbstraction()
    {
        var services = CreateServiceCollection();

        services.Any(d => d.ServiceType == typeof(IShellAdapter)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(BashShellAdapter)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(PowerShellShellAdapter)).ShouldBeTrue();
    }

    [Fact]
    public void ConfigureServicesAsync_RegistersProjectContextLoader()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IProjectContextLoader));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(DefaultProjectContextLoader));
    }

    [Fact]
    public void ConfigureServicesAsync_RegistersDuckDuckGoSearchProvider()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWebSearchProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(DuckDuckGoSearchProvider));
    }

    #endregion

    #region TryAdd 行为

    [Fact]
    public void ConfigureServicesAsync_TryAdd_WebSearchProvider_NotOverridden()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 模拟应用程序预先注册自定义实现
        var customProvider = Mock.Of<IWebSearchProvider>();
        services.AddSingleton(customProvider);

        // 配置模块
        ConfigureModule(services);

        // TryAdd 不应覆盖已注册的服务
        var descriptor = services.First(d => d.ServiceType == typeof(IWebSearchProvider));
        descriptor.ImplementationInstance.ShouldBe(customProvider);
    }

    [Fact]
    public void ConfigureServicesAsync_DoesNotRegisterMemoryStore()
    {
        // AICoderModule 依赖 AIModule 提供 IMemoryStore，自身不再注册
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryStore));

        descriptor.ShouldBeNull();
    }

    [Fact]
    public void ConfigureServicesAsync_DoesNotRegisterToolRegistry()
    {
        // AICoderModule 依赖 AIModule 提供 IToolRegistry，自身不再注册
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IToolRegistry));

        descriptor.ShouldBeNull();
    }

    [Fact]
    public void ConfigureServicesAsync_DoesNotRegisterToolScanner()
    {
        // AICoderModule 依赖 AIModule 提供 IToolScanner，自身不再注册
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IToolScanner));

        descriptor.ShouldBeNull();
    }

    #endregion

    #region 模块元数据

    [Fact]
    public void LoadOrder_Is54()
    {
        var module = new AICoderModule();

        module.LoadOrder.ShouldBe(54);
    }

    #endregion

    #region 工具类注册

    [Fact]
    public void ConfigureServicesAsync_RegistersAllToolClasses()
    {
        var services = CreateServiceCollection();

        // 验证所有工具类都被注册（BashTools 和 PowerShellTools 均无条件注册）
        services.Any(d => d.ServiceType == typeof(FileSystemTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(ShellTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(BashTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(PowerShellTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(CodeSearchTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(GitTools)).ShouldBeTrue();
    }

    [Fact]
    public void BashTools_AndPowerShellTools_BothResolvableFromDI()
    {
        // Both are registered unconditionally so ToolAdapter can resolve them.
        // The in-method OS guard (not DI gating) is the enforcement layer.
        var services = CreateServiceCollection();
        var provider = services.BuildServiceProvider();

        provider.GetService<BashTools>().ShouldNotBeNull(
            "BashTools must be resolvable from DI on all platforms");
        provider.GetService<PowerShellTools>().ShouldNotBeNull(
            "PowerShellTools must be resolvable from DI on all platforms");
    }

    [Fact]
    public async Task WrongOsShellTools_ReturnGracefulErrorNotException()
    {
        // Invoking the wrong-OS shell tool must return a structured error object, never throw.
        // This confirms the in-method OS guard (not DI gating) is the enforcement layer.
        var services = CreateServiceCollection();
        var provider = services.BuildServiceProvider();

        if (OperatingSystem.IsWindows())
        {
            // On Windows: BashTools.ExecuteBashAsync must return a graceful error, not throw
            var bashTools = provider.GetRequiredService<BashTools>();
            var result = await bashTools.ExecuteBashAsync("echo test");
            result.ShouldNotBeNull("BashTools must return a result object on Windows, not throw");
            // Result must contain an "error" field describing platform unavailability
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            json.ShouldContain("only available");
        }
        else
        {
            // On Unix: PowerShellTools.ExecutePowerShellAsync must return a graceful error, not throw
            var psTools = provider.GetRequiredService<PowerShellTools>();
            var result = await psTools.ExecutePowerShellAsync("echo test");
            result.ShouldNotBeNull("PowerShellTools must return a result object on Unix, not throw");
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            json.ShouldContain("only available");
        }
    }

    [Fact]
    public void ConfigureServicesAsync_ToolClasses_AreSingleton()
    {
        var services = CreateServiceCollection();

        var fsDescriptor = services.First(d => d.ServiceType == typeof(FileSystemTools));
        fsDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        var shellDescriptor = services.First(d => d.ServiceType == typeof(ShellTools));
        shellDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    #endregion

    #region HttpClient 注册

    [Fact]
    public void ConfigureServicesAsync_RegistersNamedHttpClients()
    {
        var services = CreateServiceCollection();

        // HttpClient 通过 AddHttpClient 注册为 IHttpClientFactory
        var hasHttpFactory = services.Any(d => d.ServiceType == typeof(IHttpClientFactory));
        hasHttpFactory.ShouldBeTrue();
    }

    #endregion

    /// <summary>
    /// 创建并配置模块的 ServiceCollection
    /// </summary>
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureModule(services);
        return services;
    }

    private static void ConfigureModule(IServiceCollection services)
    {
        var module = new AICoderModule();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Coder:ProjectRoot"] = "."
            })
            .Build();

        // 模拟 PreConfigureServicesAsync（注册选项）
        services.AddOptions<CoderOptions>()
            .Bind(configuration.GetSection("AI:Coder"));

        // 模拟 ConfigureServicesAsync
        var context = new ServiceConfigurationContext(services, configuration);
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
    }
}
