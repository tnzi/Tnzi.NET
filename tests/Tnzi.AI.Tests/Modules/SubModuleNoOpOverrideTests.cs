using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.AI.Skills;
using Tnzi.AI.Workflow;

namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// 守卫：可选子模块加载后，其真实实现必须胜出 AIModule 的 NoOp 回退。
/// <para>
/// 结构性保证（ced3e778 深修）：AIModule 把 NoOp 回退的 <c>TryAdd</c> 放在
/// <c>PostConfigureServicesAsync</c>（所有模块 Configure 之后），因此子模块在 Configure 阶段的
/// 任意注册（TryAdd 或 Add）都先于回退 → 回退见已存在即跳过，真实实现永远胜出，
/// 容器中也不应残留任何 NoOp 描述符。本测试守卫该排序不变量：若有人把回退搬回
/// ConfigureServicesAsync（或子模块意外丢失注册），断言会失败。
/// </para>
/// </summary>
public class SubModuleNoOpOverrideTests
{
    /// <summary>
    /// AISkillsModule 加载后，这些技能服务必须解析为真实实现而非 NoOp 回退，
    /// 且容器中不应有任何 NoOp 描述符（PostConfigure 的 TryAdd 应已跳过）。
    /// </summary>
    [Theory]
    [InlineData(typeof(ISkillTemplateEngine))]
    [InlineData(typeof(ISkillConstraintEnforcer))]
    [InlineData(typeof(ISkillRequirementsValidator))]
    [InlineData(typeof(ISkillSearchService))]
    [InlineData(typeof(ISkillService))]
    [InlineData(typeof(ISkillStore))]
    [InlineData(typeof(ISkillLoadTracker))]
    [InlineData(typeof(ISkillRegistry))]
    public void Skills_RealImplementation_WinsOverNoOpFallback(Type serviceType)
    {
        var services = BuildServices(new AISkillsModule());

        AssertRealImplementationWins(services, serviceType);
    }

    /// <summary>
    /// AIWorkflowModule 加载后，工作流服务必须解析为真实实现而非 NoOp 回退。
    /// </summary>
    [Theory]
    [InlineData(typeof(IWorkflowService))]
    [InlineData(typeof(IWorkflowExecutionControlService))]
    [InlineData(typeof(IWorkflowExecutionQueryService))]
    [InlineData(typeof(IWorkflowExecutionMailbox))]
    [InlineData(typeof(IWorkflowCheckpointStore))]
    public void Workflow_RealImplementation_WinsOverNoOpFallback(Type serviceType)
    {
        var services = BuildServices(new AIWorkflowModule());

        AssertRealImplementationWins(services, serviceType);
    }

    /// <summary>
    /// 历史 bug 形态的回归测试（ced3e778）：子模块在 Configure 阶段用 <c>TryAdd</c> 注册真实实现，
    /// 回退在 PostConfigure 阶段才注册 → TryAdd 不再被 NoOp 抢占，真实实现胜出。
    /// 若有人把回退搬回 Configure 阶段，此测试立刻变红。
    /// </summary>
    [Fact]
    public void TryAdd_InConfigurePhase_BeatsNoOpFallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration();
        var context = new Tnzi.Modules.ServiceConfigurationContext(services, configuration);

        var aiModule = new AIModule();
        aiModule.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        // 模拟子模块（或应用插件模块）在 Configure 阶段以 TryAdd 注册真实实现 - 历史上被 NoOp 抢占的形态
        services.TryAddSingleton<ISkillTemplateEngine, SkillTemplateEngine>();

        aiModule.PostConfigureServicesAsync(context).GetAwaiter().GetResult();

        var descriptors = services.Where(d => d.ServiceType == typeof(ISkillTemplateEngine)).ToList();
        descriptors.Count.ShouldBe(1, "the PostConfigure NoOp TryAdd must skip when a real registration exists");
        descriptors[0].ImplementationType.ShouldBe(typeof(SkillTemplateEngine));
    }

    /// <summary>
    /// 纯契约组合（仅 AIModule，无任何子模块）下，每个可选子模块接口都必须有 NoOp 回退注册 -
    /// 守卫回退集合没有在 PostConfigure 迁移中丢失。
    /// </summary>
    [Theory]
    [InlineData(typeof(IWorkflowService))]
    [InlineData(typeof(IWorkflowExecutionControlService))]
    [InlineData(typeof(IWorkflowExecutionQueryService))]
    [InlineData(typeof(IWorkflowExecutionMailbox))]
    [InlineData(typeof(IAgentStreamForwarder))]
    [InlineData(typeof(IVectorStore))]
    [InlineData(typeof(ISkillLoadTracker))]
    [InlineData(typeof(ISkillService))]
    [InlineData(typeof(ISkillStore))]
    [InlineData(typeof(ISkillTemplateEngine))]
    [InlineData(typeof(ISkillConstraintEnforcer))]
    [InlineData(typeof(ISkillSearchService))]
    [InlineData(typeof(ISkillRequirementsValidator))]
    [InlineData(typeof(ITextSearchService))]
    public void ContractsOnly_NoOpFallback_IsRegistered(Type serviceType)
    {
        var services = BuildServices(subModule: null);

        var descriptor = services.LastOrDefault(d => d.ServiceType == serviceType);
        descriptor.ShouldNotBeNull(
            $"{serviceType.Name} must have a NoOp fallback registration when only AIModule is loaded");
    }

    /// <summary>
    /// 子模块加载后，目标接口在容器中：(1) 有且仅有子模块的那一条注册（PostConfigure 的 TryAdd 已跳过），
    /// (2) 没有任何 NoOp 类型的描述符。对工厂注册同样有效 - 不再有「工厂描述符跳过断言」的盲区。
    /// </summary>
    private static void AssertRealImplementationWins(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();

        descriptors.ShouldNotBeEmpty($"{serviceType.Name} should be registered");
        descriptors.Count.ShouldBe(1,
            $"{serviceType.Name} should have exactly the sub-module's registration - a second descriptor means " +
            "AIModule's NoOp fallback was registered despite the sub-module being loaded (fallbacks must stay in " +
            "PostConfigureServicesAsync so their TryAdd skips)");

        var implType = descriptors[0].ImplementationType ?? descriptors[0].ImplementationInstance?.GetType();
        if (implType != null)
        {
            typeof(INoOpService).IsAssignableFrom(implType).ShouldBeFalse(
                $"{serviceType.Name} resolves to NoOp fallback {implType.Name} even though its sub-module is loaded");
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:openai:ApiKey"] = "test-key",
                ["AI:Providers:openai:DefaultModel"] = "gpt-4o"
            })
            .Build();
    }

    private static IServiceCollection BuildServices(Tnzi.Modules.ITnziModule? subModule)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = BuildConfiguration();

        // 真实分阶段语义：所有模块 Configure → 所有模块 PostConfigure（AIModule 的回退在 Post 阶段注册）
        Tnzi.Modules.ITnziModule[] modules = subModule == null
            ? [new AIModule()]
            : [new AIModule(), subModule];
        TestHelpers.ConfigureModules(services, configuration, modules);

        return services;
    }
}
