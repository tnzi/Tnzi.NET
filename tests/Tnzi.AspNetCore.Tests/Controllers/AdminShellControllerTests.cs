using System.Reflection;
using Tnzi.AspNetCore.Controllers;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Controllers;

/// <summary>
/// DefaultAdminShellController.GetModules 端点测试 - 验证只返回业务模块
/// (TnziApplicationModule)、短名提取(去 Tnzi. 前缀)与 isEnabled 透传。
/// 该端点是 admin 前端 module-availability gating 的权威信号,无类级权限码
/// (任何已登录 admin 用户可读),所以对超管/权限豁免路径同样成立。
/// </summary>
public class AdminShellControllerTests
{
    private static IModuleDescriptor Descriptor(ITnziModule instance, Assembly assembly, bool enabled)
    {
        var d = new Mock<IModuleDescriptor>();
        d.SetupGet(x => x.Instance).Returns(instance);
        d.SetupGet(x => x.Assembly).Returns(assembly);
        d.SetupGet(x => x.IsEnabled).Returns(enabled);
        return d.Object;
    }

    [Fact]
    public void GetModules_ReturnsOnlyBusinessModules_WithShortNameAndEnabledFlag()
    {
        var modules = new List<IModuleDescriptor>
        {
            // Business module (TnziApplicationModule) in the Tnzi.AspNetCore assembly → "AspNetCore", enabled.
            Descriptor(new FakeAppModule(), typeof(DefaultAdminShellController).Assembly, enabled: true),
            // Core/infra module (NOT a TnziApplicationModule) → filtered out.
            Descriptor(new FakeCoreModule(), typeof(DefaultAdminShellController).Assembly, enabled: true),
            // Business module in the core Tnzi assembly → short name "Tnzi", disabled.
            Descriptor(new FakeAppModule(), typeof(ITnziApplication).Assembly, enabled: false),
        };
        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(modules);

        var result = new DefaultAdminShellController().GetModules(app.Object);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var byName = result.Data!.Modules.ToDictionary(m => m.Name);

        // Business module surfaced with its short name + enabled flag.
        Assert.True(byName.ContainsKey("AspNetCore"));
        Assert.True(byName["AspNetCore"].IsEnabled);

        // Second business module (core assembly) → short name "Tnzi", disabled flag preserved.
        Assert.True(byName.ContainsKey("Tnzi"));
        Assert.False(byName["Tnzi"].IsEnabled);

        // The non-application module is NOT reported - gating only cares about
        // business modules, which map to the front-end's top-level module routes.
        Assert.Equal(2, result.Data.Modules.Count);
    }

    [Fact]
    public void GetModules_FoldsDuplicateShortNames_EnabledIfAny()
    {
        // Two descriptors resolving to the same short name - the endpoint folds
        // them into one row (enabled wins).
        var modules = new List<IModuleDescriptor>
        {
            Descriptor(new FakeAppModule(), typeof(DefaultAdminShellController).Assembly, enabled: false),
            Descriptor(new FakeAppModule(), typeof(DefaultAdminShellController).Assembly, enabled: true),
        };
        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(modules);

        var result = new DefaultAdminShellController().GetModules(app.Object);

        var single = Assert.Single(result.Data!.Modules);
        Assert.Equal("AspNetCore", single.Name);
        Assert.True(single.IsEnabled);
    }

    private sealed class FakeAppModule : TnziApplicationModule
    {
    }

    private sealed class FakeCoreModule : TnziCoreModule
    {
    }
}
