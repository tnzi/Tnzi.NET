using Microsoft.AspNetCore.Mvc;
using Tnzi.Results;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Controllers.Admin;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 「宿主没有加载 <c>Tnzi.Storage.Workspace</c>」时，留在父模块控制器上的那三组端点该怎么表现。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这些用例住在<b>父测试项目</b>是刻意的：本项目<b>不引用</b>那个可选包，所以它演的是
/// 真实的「忘了加载」现场，而不是一个把服务设成 null 模拟出来的现场。
/// </para>
/// <para>
/// 两件事要分开钉住：①<b>控制器仍然造得出来</b> —— 三个可选参数带默认值，
/// <c>ActivatorUtilities</c>（MVC 激活控制器的那条路）能在容器里没有这些服务时照常构造；
/// 造不出来的话表现是<b>请求 500</b>，那是坏行为不是少能力。
/// ②<b>调用那些端点得到 501 并指名要加载什么</b> —— 不是 404（路由确实在，
/// 回 404 会让调用方去查一个不存在的拼写问题），也不是安静地成功。
/// </para>
/// </remarks>
public class WorkspaceAbsenceTests
{
    /// <summary>只注册父模块自己有的东西，刻意<b>不</b>注册工作区那三个服务。</summary>
    private static IServiceProvider BuildHostWithoutWorkspace()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IFileStorageService>());
        services.AddSingleton(Mock.Of<IFileReferenceService>());
        return services.BuildServiceProvider();
    }

    private static DefaultStorageController CreateUserController()
        => ActivatorUtilities.CreateInstance<DefaultStorageController>(BuildHostWithoutWorkspace());

    private static DefaultStorageAdminController CreateAdminController()
        => ActivatorUtilities.CreateInstance<DefaultStorageAdminController>(BuildHostWithoutWorkspace());

    [Fact]
    public void UserController_IsConstructible_WithoutTheWorkspacePackage()
    {
        // 这一条守的是「启动/激活不炸」。三个可选参数一旦丢掉默认值，
        // 没加载工作区包的宿主每次请求 files/* 都会 500 —— 而不是少几个端点。
        var controller = CreateUserController();

        Assert.NotNull(controller);
    }

    [Fact]
    public void AdminController_IsConstructible_WithoutTheWorkspacePackage()
    {
        Assert.NotNull(CreateAdminController());
    }

    [Fact]
    public async Task ShareEndpoints_Return501_NamingThePackage()
    {
        var controller = CreateUserController();

        var create = await controller.CreateShare(Guid.NewGuid(), new CreateShareRequest());
        var preview = await controller.GetSharePreview("token");
        var get = await controller.GetShare("token");
        var revoke = await controller.RevokeShare("token");
        var verify = await controller.VerifyShareAccess("token", new VerifyShareRequest());

        AssertUnavailable(create.Code, create.Message);
        AssertUnavailable(preview.Code, preview.Message);
        AssertUnavailable(get.Code, get.Message);
        AssertUnavailable(revoke.Code, revoke.Message);
        AssertUnavailable(verify.Code, verify.Message);
    }

    [Fact]
    public async Task VersionEndpoints_Return501_NamingThePackage()
    {
        var controller = CreateUserController();

        var list = await controller.GetVersions(Guid.NewGuid());
        var restore = await controller.RestoreVersion(Guid.NewGuid(), 1);
        var delete = await controller.DeleteVersion(Guid.NewGuid(), 1);

        AssertUnavailable(list.Code, list.Message);
        AssertUnavailable(restore.Code, restore.Message);
        AssertUnavailable(delete.Code, delete.Message);
    }

    [Fact]
    public async Task ChunkedUploadEndpoints_Return501_NamingThePackage()
    {
        var controller = CreateUserController();

        var init = await controller.InitiateChunkedUpload(new InitiateChunkedUploadRequest { FileName = "a.txt", TotalSize = 1 });
        var complete = await controller.CompleteChunkedUpload(Guid.NewGuid());
        var cancel = await controller.CancelChunkedUpload(Guid.NewGuid());
        var progress = await controller.GetUploadProgress(Guid.NewGuid());

        AssertUnavailable(init.Code, init.Message);
        AssertUnavailable(complete.Code, complete.Message);
        AssertUnavailable(cancel.Code, cancel.Message);
        AssertUnavailable(progress.Code, progress.Message);
    }

    [Fact]
    public async Task AdminShareEndpoints_Return501_NamingThePackage()
    {
        var controller = CreateAdminController();

        var byFile = await controller.GetSharesByFile(Guid.NewGuid());
        var query = await controller.QueryActiveShares(new ActiveSharesQueryRequest());
        var revoke = await controller.BatchRevokeShares([Guid.NewGuid()]);

        AssertUnavailable(byFile.Code, byFile.Message);
        AssertUnavailable(query.Code, query.Message);
        AssertUnavailable(revoke.Code, revoke.Message);
    }

    [Fact]
    public async Task StreamingShareDownload_Returns501_RatherThanCrashing()
    {
        // 这两个端点返回 IActionResult 而不是 ApiResult，所以走的是另一条分支；
        // 漏掉守卫的表现是 NullReferenceException（500），不是 501。
        var controller = CreateUserController();

        var download = await controller.DownloadByShareToken("token");
        var versionDownload = await controller.DownloadVersion(Guid.NewGuid(), 1);

        Assert.Equal(StatusCodes.Status501NotImplemented, Assert.IsType<ObjectResult>(download).StatusCode);
        Assert.Equal(StatusCodes.Status501NotImplemented, Assert.IsType<ObjectResult>(versionDownload).StatusCode);
    }

    [Fact]
    public async Task CoreFileEndpoints_KeepWorking_WithoutTheWorkspacePackage()
    {
        // 对照组：缺席只该少掉工作区那三组能力。核心文件端点必须照常工作 ——
        // 否则「少一个可选包」就变成了「存储模块坏了」。
        // 刻意挑一个不经 Mapster 投影的端点：那条路要求宿主初始化过 Mapper，
        // 而这里要证明的是「服务解析与调用照常」，不是映射配置。
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetUrlAsync(It.IsAny<Guid>(), It.IsAny<int?>()))
            .ReturnsAsync(Result.Success<string>("https://example.test/files/a.txt"));

        var services = new ServiceCollection();
        services.AddSingleton(storage.Object);
        var controller = ActivatorUtilities.CreateInstance<DefaultStorageController>(services.BuildServiceProvider());

        var result = await controller.GetUrl(Guid.NewGuid());

        Assert.True(result.Success);
        Assert.Equal("https://example.test/files/a.txt", result.Data);
    }

    private static void AssertUnavailable(int code, string? message)
    {
        Assert.Equal(501, code);
        Assert.NotNull(message);
        Assert.Contains("Tnzi.Storage.Workspace", message!, StringComparison.Ordinal);
    }
}
