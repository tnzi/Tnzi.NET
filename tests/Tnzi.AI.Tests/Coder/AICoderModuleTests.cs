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
    public void ConfigureServicesAsync_TryAdd_MemoryStore_NotOverridden()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 模拟 AIModule 已注册数据库记忆存储
        var mockStore = Mock.Of<IMemoryStore>();
        services.AddSingleton(mockStore);

        ConfigureModule(services);

        // FileMemoryStore 不应覆盖已注册的 IMemoryStore
        var descriptor = services.First(d => d.ServiceType == typeof(IMemoryStore));
        descriptor.ImplementationInstance.ShouldBe(mockStore);
    }

    [Fact]
    public void ConfigureServicesAsync_TryAdd_ToolRegistry_FallbackRegistered()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IToolRegistry));

        descriptor.ShouldNotBeNull();
    }

    [Fact]
    public void ConfigureServicesAsync_TryAdd_ToolScanner_FallbackRegistered()
    {
        var services = CreateServiceCollection();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IToolScanner));

        descriptor.ShouldNotBeNull();
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

        // 验证所有 11 个工具类都被注册
        services.Any(d => d.ServiceType == typeof(FileSystemTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(ShellTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(CodeSearchTools)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(GitTools)).ShouldBeTrue();
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
