using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AI.Options;
using Tnzi.AI.Services;
using Tnzi.Modules;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// <see cref="IAiUtility"/> 的跨程序集注册契约门禁。
/// </summary>
/// <remarks>
/// <para>
/// 契约与一个 OpenAI 兼容的默认实现在框架核心（<c>CoreServicesModule</c>，LoadOrder 0），
/// 目的是让只需要「预置提示词 + 一个问题」的消费应用不必加载 <c>Tnzi.AI</c>，
/// 从而不产生任何 AI 实体表、默认控制器与权限码。
/// </para>
/// <para>
/// 这道门禁守的是那个接缝的<b>另一半</b>：加载了 <c>Tnzi.AI</c> 的应用必须升级到
/// 走 <c>IChatClientFactory</c> 的实现（原生 Anthropic 协议、数据库来源的提供商、
/// 降级链、thinking 都在那一侧）。这个升级靠 <c>AIModule</c> 里的
/// <c>RemoveAll + Add</c> 完成 —— 一旦有人按框架里更常见的 <c>TryAdd</c> 惯例改写它，
/// 注册会被核心那条抢先的 <c>TryAdd</c> 静默挡掉：**服务照样解析得到、调用照样成功、
/// 只是永远走不到 Anthropic 那条协议分支**。没有任何现有测试会因此变红。
/// </para>
/// </remarks>
public class AiUtilityRegistrationTests
{
    /// <summary>
    /// 核心必须提供 <see cref="IAiUtility"/> 的默认实现 —— 消费方零 <c>[DependsOn]</c> 即可注入。
    /// </summary>
    [Fact]
    public void Core_ProvidesDefaultAiUtility_WithoutAnyAiModule()
    {
        var coreOnly = ModuleTestHelper.LoadAndCollectServiceMap<CoreOnlyStartupModule>();

        AssertNoConfigurationFailures(coreOnly);

        var descriptor = SingleAiUtilityDescriptor(coreOnly);

        Assert.Equal(typeof(OpenAiCompatibleAiUtility), descriptor.ImplementationType);

        // 能力到位的同时，代价必须为零：这个图里不该出现任何 AI 程序集 ——
        // 它们才是实体表、默认控制器与权限码的来源，也正是这次下沉要避开的东西。
        var aiModules = coreOnly.Modules
            .Select(m => m.Type.Assembly.GetName().Name)
            .Where(name => name != null && name.StartsWith("Tnzi.AI", StringComparison.Ordinal))
            .ToList();

        Assert.True(aiModules.Count == 0,
            "the core-only graph must not pull in any AI module, found: " + string.Join(", ", aiModules));
    }

    /// <summary>
    /// 加载 <c>Tnzi.AI</c> 后，实现必须被替换为走 <c>IChatClientFactory</c> 的那个。
    /// </summary>
    [Fact]
    public void AiModule_ReplacesCoreDefault_RatherThanBeingSuppressedByIt()
    {
        var graph = ArchitectureModuleGraph.Load();

        var descriptor = SingleAiUtilityDescriptor(graph);

        Assert.NotEqual(typeof(OpenAiCompatibleAiUtility), descriptor.ImplementationType);
        Assert.Equal("AiUtilityService", descriptor.ImplementationType?.Name);
        Assert.Equal("Tnzi.AI", descriptor.ImplementationType?.Assembly.GetName().Name);
    }

    /// <summary>
    /// 覆盖必须是替换而不是叠加：多条注册时 DI 取最后一条，语义变成隐式的顺序依赖。
    /// </summary>
    [Fact]
    public void AiUtility_HasExactlyOneRegistration_InTheFullGraph()
    {
        var graph = ArchitectureModuleGraph.Load();

        var registrations = graph.FinalServices
            .Where(d => d.ServiceType == typeof(IAiUtility))
            .ToList();

        Assert.Single(registrations);
    }

    /// <summary>
    /// 提供商注册表由核心绑定，且必须只绑一次 —— 重复绑定会让同一个验证器跑两遍。
    /// </summary>
    [Fact]
    public void ProviderRegistryOptions_IsValidatedExactlyOnce()
    {
        var graph = ArchitectureModuleGraph.Load();

        AssertSingleValidator<AiProviderRegistryOptions>(graph);
        AssertSingleValidator<AiUtilityOptions>(graph);
    }

    private static ServiceDescriptor SingleAiUtilityDescriptor(ModuleLoadResult result)
    {
        var registrations = result.FinalServices
            .Where(d => d.ServiceType == typeof(IAiUtility))
            .ToList();

        Assert.True(registrations.Count == 1,
            $"expected exactly one IAiUtility registration, found {registrations.Count}: " +
            string.Join(", ", registrations.Select(d => d.ImplementationType?.Name ?? "<factory>")));

        return registrations[0];
    }

    private static void AssertSingleValidator<TOptions>(ModuleLoadResult result) where TOptions : class
    {
        var validators = result.FinalServices
            .Where(d => d.ServiceType == typeof(IValidateOptions<TOptions>))
            .ToList();

        Assert.True(validators.Count == 1,
            $"expected exactly one IValidateOptions<{typeof(TOptions).Name}>, found {validators.Count}");
    }

    private static void AssertNoConfigurationFailures(ModuleLoadResult result)
    {
        Assert.True(result.Failures.Count == 0,
            "module configuration failed: " + string.Join(" | ", result.Failures.Select(f => f.ToString())));
    }

    /// <summary>
    /// 只依赖框架核心服务模块的启动模块 —— 用来证明「零 AI 依赖也能注入 IAiUtility」。
    /// </summary>
    /// <remarks>
    /// <c>CoreServicesModule</c> 必须显式声明：它并非「无条件自动加载」，而是经
    /// <c>AspNetCoreModule</c> / <c>DependencyInjectionModule</c> 的 <c>[DependsOn]</c>
    /// 进入模块图的，任何真实应用都会经这两者之一带上它。这里只声明它一个，
    /// 是为了让本用例的覆盖面就是「最小的核心」，不掺任何别的模块。
    /// </remarks>
    [DependsOn(typeof(CoreServicesModule))]
    private sealed class CoreOnlyStartupModule : TnziCustomModule
    {
    }
}
