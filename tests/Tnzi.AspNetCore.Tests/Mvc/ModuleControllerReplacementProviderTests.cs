using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Tnzi.AspNetCore.Mvc.Conventions;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 模块默认 Controller 的替换语义：谁替换谁，以及<b>谁不该被顺手删掉</b>。
/// </summary>
/// <remarks>
/// 这里守的是一个真实存在过的缺陷：<see cref="DefaultControllerAttribute"/> 是
/// <c>Inherited = false</c> 而 <c>[Route]</c> 是 <c>Inherited = true</c>，
/// 于是消费方按框架文档写的子类会被判成「用户 Controller」，
/// 一旦按「有用户 Controller 就删掉整组默认版本」处理，
/// 同路由上**其它模块**的默认 Controller 会被静默删除。
/// 拆分子模块之后同路由多模块共存会变常态，这条语义因此是拆分的前提。
/// </remarks>
public class ModuleControllerReplacementProviderTests
{
    [DefaultController]
    [Route("chat")]
    private class ModuleAChatController;

    /// <summary>另一个模块在同一路由模板上的默认 Controller（真实例：AI 与 Chat 都用 <c>chat</c>）。</summary>
    [DefaultController]
    [Route("chat")]
    private sealed class ModuleBChatConfigController;

    /// <summary>消费方按文档覆写 A：只继承，不重复标注 <c>[Route]</c>（它是 Inherited=true）。</summary>
    private sealed class AppChatController : ModuleAChatController;

    /// <summary>消费方自带的、与任何默认版本无继承关系的 Controller。</summary>
    [Route("chat")]
    private sealed class StandaloneAppChatController;

    private static List<ControllerModel> Run(params Type[] controllerTypes)
    {
        var provider = new ModuleControllerReplacementProvider(new ControllerActivationDiagnostics());
        var context = new ApplicationModelProviderContext(controllerTypes.Select(t => t.GetTypeInfo()).ToList());
        foreach (var type in controllerTypes)
        {
            context.Result.Controllers.Add(new ControllerModel(type.GetTypeInfo(), []));
        }

        provider.OnProvidersExecuting(context);
        return context.Result.Controllers.ToList();
    }

    /// <summary>两个模块的默认 Controller 同路由共存：谁都不该被删。</summary>
    [Fact]
    public void TwoDefaults_SameRoute_BothSurvive()
    {
        var survivors = Run(typeof(ModuleAChatController), typeof(ModuleBChatConfigController));

        Assert.Equal(2, survivors.Count);
    }

    /// <summary>消费方子类覆写：被继承的那个默认版本让位（既有约定，不能回归）。</summary>
    [Fact]
    public void AppSubclass_ReplacesTheDefaultItDerivesFrom()
    {
        var survivors = Run(typeof(ModuleAChatController), typeof(AppChatController));

        Assert.Single(survivors);
        Assert.Equal(typeof(AppChatController), survivors[0].ControllerType.AsType());
    }

    /// <summary>
    /// ★核心断言：子类只替换它的基类，同路由上另一个模块的默认 Controller 必须活着。
    /// </summary>
    [Fact]
    public void AppSubclass_DoesNotDeleteAnotherModulesDefault()
    {
        var survivors = Run(
            typeof(ModuleAChatController),
            typeof(ModuleBChatConfigController),
            typeof(AppChatController));

        Assert.DoesNotContain(survivors, c => c.ControllerType.AsType() == typeof(ModuleAChatController));
        Assert.Contains(survivors, c => c.ControllerType.AsType() == typeof(ModuleBChatConfigController));
        Assert.Contains(survivors, c => c.ControllerType.AsType() == typeof(AppChatController));
    }

    /// <summary>
    /// 与任何默认版本无继承关系的用户 Controller 仍整组接管 —— 这是文档写明的覆盖方式，语义不变。
    /// </summary>
    [Fact]
    public void StandaloneAppController_StillTakesOverTheWholeRoute()
    {
        var survivors = Run(
            typeof(ModuleAChatController),
            typeof(ModuleBChatConfigController),
            typeof(StandaloneAppChatController));

        Assert.Single(survivors);
        Assert.Equal(typeof(StandaloneAppChatController), survivors[0].ControllerType.AsType());
    }

    /// <summary>没有用户 Controller 时不动任何东西。</summary>
    [Fact]
    public void DefaultsAlone_AreNeverRemoved()
    {
        var survivors = Run(typeof(ModuleAChatController));

        Assert.Single(survivors);
    }
}
