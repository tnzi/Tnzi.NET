using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Mvc.Conventions;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 条件控制器提供者：按构造依赖的可用性决定 Controller 是否活着。
/// </summary>
public class ConditionalControllerProviderTests
{
    private interface IMissingService;

    private interface IOptionalContributor;

    /// <summary>依赖一个谁也没注册的服务 —— 应当被移除。</summary>
    private sealed class NeedsMissingServiceController
    {
        public NeedsMissingServiceController(IMissingService service) => _ = service;
    }

    /// <summary>
    /// 依赖"零个或多个实现"的集合注入 —— 必须活着。
    /// </summary>
    /// <remarks>
    /// 这是可选多实现扩展点的标准形态（税务申报映射器 / 过账守卫 / 总账搜索贡献者）。
    /// 一个都没注册恰恰是它要表达的状态，此时端点该回 501 引导，而不是整条路由消失
    /// 成 404 —— 后者会让人以为版本装错了。
    /// </remarks>
    private sealed class NeedsContributorsController
    {
        public NeedsContributorsController(IEnumerable<IOptionalContributor> contributors) => _ = contributors;
    }

    /// <summary>
    /// <c>IReadOnlyList&lt;T&gt;</c> / <c>T[]</c> —— MS.DI **不**为它们做多实现特例，
    /// 零实现时激活期直接抛异常，所以按依赖缺失移除是对的。
    /// </summary>
    private sealed class NeedsReadOnlyListController
    {
        public NeedsReadOnlyListController(IReadOnlyList<IOptionalContributor> contributors) => _ = contributors;
    }

    private sealed class NeedsArrayController
    {
        public NeedsArrayController(IOptionalContributor[] contributors) => _ = contributors;
    }

    private static ApplicationModelProviderContext Run(params Type[] controllerTypes)
    {
        var services = new ServiceCollection();
        var provider = new ConditionalControllerProvider(services, new ControllerActivationDiagnostics());

        var context = new ApplicationModelProviderContext(controllerTypes.Select(t => t.GetTypeInfo()).ToList());
        foreach (var type in controllerTypes)
        {
            context.Result.Controllers.Add(new ControllerModel(type.GetTypeInfo(), []));
        }

        provider.OnProvidersExecuting(context);
        return context;
    }

    [Fact]
    public void Controller_WithUnregisteredDependency_IsRemoved()
    {
        var context = Run(typeof(NeedsMissingServiceController));

        Assert.Empty(context.Result.Controllers);
    }

    /// <summary>
    /// ★MS.DI 对 <c>IEnumerable&lt;T&gt;</c> 在零实现时给出空集合，从不解析失败。
    /// 按"元素类型有没有注册"判定会静默移除一个完全能跑的 Controller。
    /// </summary>
    [Fact]
    public void Controller_WithEmptyEnumerableDependency_Survives()
    {
        var context = Run(typeof(NeedsContributorsController));

        Assert.Single(context.Result.Controllers);
    }

    /// <summary>
    /// <c>IReadOnlyList&lt;T&gt;</c> / <c>T[]</c> 不在 MS.DI 的多实现特例内 —— 放行它们
    /// 会把"路由不存在（404）"换成"每次调用 500"。
    /// </summary>
    [Theory]
    [InlineData(typeof(NeedsReadOnlyListController))]
    [InlineData(typeof(NeedsArrayController))]
    public void Controller_WithUnsupportedCollectionDependency_IsRemoved(Type controllerType)
    {
        var context = Run(controllerType);

        Assert.Empty(context.Result.Controllers);
    }

    /// <summary>
    /// 这条判定的**依据**：拿真实容器解析一次，而不是相信一句注释。
    /// </summary>
    /// <remarks>
    /// 上面两个测试断言的是提供者的取舍，锁不住"MS.DI 到底能不能解析"这个前提；
    /// 前提写错时它们会一起变成绿色的谎。所以在这里直接问容器。
    /// </remarks>
    [Fact]
    public void MsDi_ResolvesOnlyEnumerable_ForZeroImplementations()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();

        Assert.Empty(sp.GetRequiredService<IEnumerable<IOptionalContributor>>());
        Assert.Null(sp.GetService<IReadOnlyList<IOptionalContributor>>());
        Assert.Null(sp.GetService<IOptionalContributor[]>());
    }
}
