using System.Reflection;
using Tnzi.Authorization.Controllers.Admin;
using Tnzi.Modules;

namespace Tnzi.Authorization.Tests.Controllers;

/// <summary>
/// Locks the transient <c>IsBuiltIn</c> flag the module admin list endpoint
/// stamps from the running module graph: framework modules (code matches a
/// loaded <c>Tnzi.*</c> module) are flagged built-in, a consumer application's
/// own modules are not. The role-permission matrix relies on this to list the
/// consumer's own permissions first and separate the built-in catalogue.
/// </summary>
public class DefaultModuleAdminControllerTests
{
    [Fact]
    public async Task GetModules_stamps_isBuiltIn_from_loaded_framework_modules()
    {
        var modules = new List<FunctionModule>
        {
            new() { Code = "authorization", Name = "Authorization" }, // framework (Tnzi.Authorization is loaded)
            new() { Code = "shop", Name = "Shop" },               // a consumer application's own module
        };
        var svc = new Mock<IModuleManagementService>();
        svc.Setup(s => s.GetModulesAsync())
            .ReturnsAsync(Result<IEnumerable<FunctionModule>>.Success(modules));

        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(new List<IModuleDescriptor>
        {
            FrameworkModuleDescriptor(typeof(AuthorizationPermissions).Assembly), // "Tnzi.Authorization" → "authorization"
        });

        var result = await new DefaultModuleAdminController(svc.Object).GetModules(app.Object);

        result.Success.ShouldBeTrue();
        var byCode = result.Data!.ToDictionary(m => m.Code);
        byCode["authorization"].IsBuiltIn.ShouldBeTrue();
        byCode["shop"].IsBuiltIn.ShouldBeFalse();
    }

    private static IModuleDescriptor FrameworkModuleDescriptor(Assembly assembly)
    {
        var d = new Mock<IModuleDescriptor>();
        d.SetupGet(x => x.Instance).Returns(new FakeAppModule());
        d.SetupGet(x => x.Assembly).Returns(assembly);
        return d.Object;
    }

    private sealed class FakeAppModule : TnziApplicationModule
    {
    }
}
