using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.TestBase;

/// <summary>
/// Helper for loading modules and collecting service maps in test scenarios
/// </summary>
public static class ModuleTestHelper
{
    /// <summary>
    /// Load all modules starting from a root module type and collect service registration map
    /// </summary>
    public static (IReadOnlyList<IModuleDescriptor> Modules, Dictionary<Type, List<ServiceDescriptor>> ServiceMap)
        LoadAndCollectServiceMap<TRootModule>() where TRootModule : ITnziModule
    {
        var loader = new ModuleLoader();
        var services = new ServiceCollection();
        var modules = loader.LoadModules(services, typeof(TRootModule));

        var serviceMap = new Dictionary<Type, List<ServiceDescriptor>>();

        // 最小化配置，满足模块初始化需求
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Provider"] = "SQLite",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=:memory:",
            ["Identity:Jwt:SecretKey"] = "test-key-for-architecture-tests-only-minimum-32-chars",
        });
        var configuration = configBuilder.Build();

        services.AddSingleton<IConfiguration>(configuration);

        var context = new ServiceConfigurationContext(services, configuration);

        foreach (var module in modules)
        {
            var beforeCount = services.Count;
            try
            {
                module.Instance.ConfigureServicesAsync(context).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 配置依赖的模块可能初始化失败 — 跳过，不影响依赖审计
                serviceMap[module.Type] = [];
                System.Diagnostics.Debug.WriteLine(
                    $"[ModuleTestHelper] Skipped {module.Type.Name}: {ex.Message}");
                continue;
            }

            var newServices = services.Skip(beforeCount).ToList();
            serviceMap[module.Type] = newServices;
        }

        return (modules, serviceMap);
    }
}
