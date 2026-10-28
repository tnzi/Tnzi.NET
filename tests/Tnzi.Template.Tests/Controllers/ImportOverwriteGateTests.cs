using Tnzi.Security.Authorization;

namespace Tnzi.Template.Tests.Controllers;

/// <summary>
/// 带 <c>overwriteExisting = true</c> 的导入就是改写已有记录，除了 <c>.create</c> 还要 <c>.update</c>。
/// </summary>
/// <remarks>
/// 导入端点的特性只要求 <c>.create</c>，覆盖开关在请求体里、特性按不到它。此前只被授予新建的角色带上这个开关，
/// 就能改写每一条已有模板，绕过 <c>PUT</c> 端点要求的 <c>.update</c>；而模板是在服务进程里编译执行的 Razor。
/// </remarks>
public class ImportOverwriteGateTests
{
    private const string TemplateUpdate = "template.template.update";
    private const string LayoutUpdate = "template.layout.update";

    private static ControllerContext ContextWith(IPermissionChecker? checker)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (checker != null)
            services.AddSingleton(checker);
        return new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } };
    }

    private static Mock<IPermissionChecker> Granting(params string[] codes)
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync((string code) => codes.Contains(code));
        return checker;
    }

    private static (DefaultTemplateAdminController Controller, Mock<ITemplateStoreService> Store) TemplateController(IPermissionChecker? checker)
    {
        var store = new Mock<ITemplateStoreService>();
        store.Setup(s => s.ImportTemplatesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateImportResultDto>.Success(new TemplateImportResultDto()));
        return (new DefaultTemplateAdminController(store.Object) { ControllerContext = ContextWith(checker) }, store);
    }

    private static (DefaultLayoutAdminController Controller, Mock<ILayoutStoreService> Store) LayoutController(IPermissionChecker? checker)
    {
        var store = new Mock<ILayoutStoreService>();
        store.Setup(s => s.ImportLayoutsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LayoutImportResultDto>.Success(new LayoutImportResultDto()));
        return (new DefaultLayoutAdminController(store.Object) { ControllerContext = ContextWith(checker) }, store);
    }

    /// <summary>★★ 只有新建码：覆盖导入被拒，一条都不导。</summary>
    [Fact]
    public async Task OverwritingTemplates_WithoutTheUpdateCode_IsForbidden()
    {
        var (controller, store) = TemplateController(Granting("template.template.create").Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            controller.Import(new TemplateImportRequest { Json = "[]", OverwriteExisting = true }));

        store.Verify(s => s.ImportTemplatesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OverwritingTemplates_WithTheUpdateCode_Imports()
    {
        var (controller, store) = TemplateController(Granting("template.template.create", TemplateUpdate).Object);

        await controller.Import(new TemplateImportRequest { Json = "[]", OverwriteExisting = true });

        store.Verify(s => s.ImportTemplatesAsync("[]", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>对照：不覆盖的导入（已有的跳过）不问 <c>.update</c>。</summary>
    [Fact]
    public async Task ImportingWithoutOverwrite_NeedsNoUpdateCode()
    {
        var checker = Granting("template.template.create");
        var (controller, store) = TemplateController(checker.Object);

        await controller.Import(new TemplateImportRequest { Json = "[]", OverwriteExisting = false });

        store.Verify(s => s.ImportTemplatesAsync("[]", false, It.IsAny<CancellationToken>()), Times.Once);
        checker.Verify(c => c.IsGrantedAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>没有权限检查器（授权模块没加载）时覆盖导入同样拒绝：deny-by-default。</summary>
    [Fact]
    public async Task OverwritingTemplates_WithoutAPermissionChecker_IsForbidden()
    {
        var (controller, store) = TemplateController(checker: null);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            controller.Import(new TemplateImportRequest { Json = "[]", OverwriteExisting = true }));

        store.Verify(s => s.ImportTemplatesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OverwritingLayouts_WithoutTheUpdateCode_IsForbidden()
    {
        var (controller, store) = LayoutController(Granting("template.layout.create").Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            controller.Import(new LayoutImportRequest { Json = "[]", OverwriteExisting = true }));

        store.Verify(s => s.ImportLayoutsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OverwritingLayouts_WithTheUpdateCode_Imports()
    {
        var (controller, store) = LayoutController(Granting("template.layout.create", LayoutUpdate).Object);

        await controller.Import(new LayoutImportRequest { Json = "[]", OverwriteExisting = true });

        store.Verify(s => s.ImportLayoutsAsync("[]", true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
