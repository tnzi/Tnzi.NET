using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.TestBase;

/// <summary>
/// 某个模块在某个生命周期阶段配置失败。
/// </summary>
/// <param name="ModuleType">出错的模块</param>
/// <param name="Phase">阶段名（PreConfigureServices / ConfigureServices / PostConfigureServices）</param>
/// <param name="Exception">原始异常</param>
public sealed record ModuleConfigurationFailure(Type ModuleType, string Phase, Exception Exception)
{
    /// <summary>单行摘要，便于直接拼进断言消息。</summary>
    public override string ToString()
        => $"{ModuleType.Name}.{Phase}: {Exception.GetType().Name}: {Exception.Message.Split('\n')[0].Trim()}";
}

/// <summary>
/// 模块加载与服务注册归属的收集结果。
/// </summary>
/// <param name="Modules">拓扑排序后的模块列表</param>
/// <param name="ServiceMap">模块 → 它注册的服务描述符</param>
/// <param name="Failures">配置阶段抛出的异常，<b>调用方必须显式处理</b></param>
public sealed record ModuleLoadResult(
    IReadOnlyList<IModuleDescriptor> Modules,
    Dictionary<Type, List<ServiceDescriptor>> ServiceMap,
    IReadOnlyList<ModuleConfigurationFailure> Failures);

/// <summary>
/// Helper for loading modules and collecting service maps in test scenarios
/// </summary>
public static class ModuleTestHelper
{
    /// <summary>
    /// Load all modules starting from a root module type and collect service registration map
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>失败不再被吞掉。</b>历史版本对 <c>ConfigureServicesAsync</c> 抛出的异常
    /// <c>catch</c> 后把该模块的服务列表置空并 <c>continue</c>，只写一行
    /// <c>Debug.WriteLine</c>（Release 下连输出都没有）。后果是「夹具坏了」和「没有违规」
    /// 长得一模一样：<c>EFCoreModule</c> 因为这个夹具自己少配了 <c>Database:DbContexts:0:Name</c>
    /// 而长期抛异常出局，依赖它的审计从来没审到过它。现在异常一律收进
    /// <see cref="ModuleLoadResult.Failures"/>，由调用方断言。
    /// </para>
    /// <para>
    /// <b>三个阶段都跑。</b>历史版本只跑 <c>ConfigureServicesAsync</c>，于是所有在
    /// <c>PostConfigureServicesAsync</c> 里注册的东西（AI 的 18 个 NoOp 回退全在那儿）
    /// 对依赖审计完全不可见。<c>Post</c> 阶段必须等全部模块的 <c>Configure</c> 跑完之后
    /// 再统一跑 —— 那是框架的真实生命周期顺序，也是 <c>TryAdd</c> 式回退能被正确覆盖的前提。
    /// </para>
    /// <para>
    /// <b>归属按引用差集算，不按下标。</b><c>services.Skip(beforeCount)</c> 那种写法假设模块
    /// 只追加注册，而 <c>RedisCachingModule</c> 实打实用了
    /// <c>services.RemoveAll&lt;ICache&gt;()</c> 再重新注册 —— 一旦有删除，下标就整体错位，
    /// 后续模块的服务会被算到别人头上。<see cref="ServiceDescriptor"/> 没有重写
    /// <c>Equals</c>，默认引用相等正好可以用来取真正的新增项。
    /// </para>
    /// </remarks>
    /// <param name="additionalConfiguration">
    /// 追加/覆盖测试配置。调用方专有的键（例如指向自己那个 DbContext 的
    /// <c>Database:DbContexts:0:DbContextType</c>）从这里传，不要写死进本共享夹具。
    /// </param>
    public static ModuleLoadResult LoadAndCollectServiceMap<TRootModule>(
        IDictionary<string, string?>? additionalConfiguration = null) where TRootModule : ITnziModule
    {
        var loader = new ModuleLoader();
        var services = new ServiceCollection();
        var modules = loader.LoadModules(services, typeof(TRootModule));

        var serviceMap = new Dictionary<Type, List<ServiceDescriptor>>();
        var failures = new List<ModuleConfigurationFailure>();

        var configuration = BuildTestConfiguration(additionalConfiguration);
        services.AddSingleton<IConfiguration>(configuration);

        var context = new ServiceConfigurationContext(services, configuration);

        // 第一遍：Pre + Configure，按拓扑序。
        var brokenModules = new HashSet<Type>();
        foreach (var module in modules)
        {
            var before = Snapshot(services);
            serviceMap[module.Type] = [];

            if (!TryRun(module, "PreConfigureServices",
                    m => m.Instance.PreConfigureServicesAsync(context), failures)
                || !TryRun(module, "ConfigureServices",
                    m => m.Instance.ConfigureServicesAsync(context), failures))
            {
                brokenModules.Add(module.Type);
                continue;
            }

            serviceMap[module.Type] = NewlyAdded(services, before);
        }

        // 第二遍：Post，必须在全部 Configure 之后 —— 这是框架的真实顺序，
        // 也是 AI 那批 TryAdd 式 NoOp 回退不会盖掉真实现的原因。
        foreach (var module in modules)
        {
            // 前一阶段就崩了的模块跳过：真实框架里 Configure 抛异常等于启动失败、
            // 根本走不到 Post。硬跑它只会产出一堆缺前置条件的次生异常，把真正的
            // 根因埋在噪音里 —— 报告质量比多收集一点信息重要。
            if (brokenModules.Contains(module.Type))
                continue;

            var before = Snapshot(services);

            if (!TryRun(module, "PostConfigureServices",
                    m => m.Instance.PostConfigureServicesAsync(context), failures))
                continue;

            var added = NewlyAdded(services, before);
            if (added.Count > 0)
                serviceMap[module.Type].AddRange(added);
        }

        return new ModuleLoadResult(modules, serviceMap, failures);
    }

    /// <summary>
    /// 按引用记录当前已注册的描述符。
    /// </summary>
    /// <remarks>
    /// 显式传 <see cref="ReferenceEqualityComparer"/>（靠 <c>IEqualityComparer&lt;in T&gt;</c>
    /// 的逆变直接当 <c>IEqualityComparer&lt;ServiceDescriptor&gt;</c> 用）：
    /// <see cref="ServiceDescriptor"/> 目前没重写 <c>Equals</c>，默认就是引用相等，
    /// 但两个模块完全可能注册出「值相等」的描述符（同 ServiceType + 同实现类型 + 同生命周期），
    /// 那时值相等会把后者误判成「已存在」而漏掉。
    /// </remarks>
    private static HashSet<ServiceDescriptor> Snapshot(IServiceCollection services)
        => new(services, ReferenceEqualityComparer.Instance);

    /// <summary>取快照之后真正新增的描述符（对 <c>RemoveAll</c> 造成的下标位移免疫）。</summary>
    private static List<ServiceDescriptor> NewlyAdded(
        IServiceCollection services, HashSet<ServiceDescriptor> before)
        => services.Where(d => !before.Contains(d)).ToList();

    private static bool TryRun(
        IModuleDescriptor module,
        string phase,
        Func<IModuleDescriptor, Task> action,
        List<ModuleConfigurationFailure> failures)
    {
        try
        {
            action(module).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            failures.Add(new ModuleConfigurationFailure(module.Type, phase, ex));
            return false;
        }
    }

    /// <summary>
    /// 满足模块初始化最低要求的配置。
    /// </summary>
    /// <remarks>
    /// <c>Database:DbContexts:0:Name</c> 不能少：<c>EFCoreModule</c> 校验每个 DbContext
    /// 配置项都要有非空 Name，缺了会抛「all DbContext configurations are invalid」。
    /// <c>StrictFrameworkRegistration</c> 打开，让任何误用自动注册标记的框架程序集
    /// 大声失败而不是在运行时被静默跳过（铁律 #1）。
    /// </remarks>
    private static IConfiguration BuildTestConfiguration(IDictionary<string, string?>? overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:Provider"] = "SQLite",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=:memory:",
            ["Identity:Jwt:SecretKey"] = "test-key-for-architecture-tests-only-minimum-32-chars",
            ["Tnzi:DependencyInjection:StrictFrameworkRegistration"] = "true",
        };

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
                settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
