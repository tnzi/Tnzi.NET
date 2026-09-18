using Tnzi.Application;
using Tnzi.Security.Authorization;

namespace Tnzi.Tests.Application;

/// <summary>
/// <see cref="ApplicationService"/> 权限辅助方法的失败方向：权限检查器未注册（Authorization 模块未加载）
/// 时必须拒绝，不能放行 —— 与 <c>RequireAnyPermissionAsync</c> / <c>RequireAllPermissionsAsync</c>
/// 及框架 deny-by-default 口径一致。
/// </summary>
/// <remarks>
/// 此前 <c>CheckPermissionAsync</c> 在检查器为 null 时直接返回：按 <c>docs/coding-standards/service.md</c>
/// 写 <c>await CheckPermissionAsync("invoice.approve")</c> 的消费方，一旦应用没加载 Authorization
/// （或注册被顶掉），审批逻辑就在 200 与零日志下继续执行。
/// </remarks>
public class ApplicationServicePermissionTests
{
    private sealed class ProbeService(IServiceProvider serviceProvider) : ApplicationService(serviceProvider)
    {
        public Task Check(string permission) => CheckPermissionAsync(permission);
    }

    [Fact]
    public async Task CheckPermissionAsync_WithoutPermissionChecker_ThrowsForbidden()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var service = new ProbeService(services);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => service.Check("invoice.approve"));

        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
        Assert.Contains("Authorization", ex.Message);
    }

    [Fact]
    public async Task CheckPermissionAsync_WithChecker_DelegatesToCheckAsync()
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.CheckAsync("invoice.approve")).Returns(Task.CompletedTask).Verifiable();
        var services = new ServiceCollection().AddSingleton(checker.Object).BuildServiceProvider();
        var service = new ProbeService(services);

        await service.Check("invoice.approve");

        checker.Verify(c => c.CheckAsync("invoice.approve"), Times.Once);
    }

    [Fact]
    public async Task CheckPermissionAsync_WithChecker_PropagatesDenial()
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.CheckAsync("invoice.approve"))
            .ThrowsAsync(new ForbiddenException("Permission denied: invoice.approve", ErrorCodes.FORBIDDEN));
        var services = new ServiceCollection().AddSingleton(checker.Object).BuildServiceProvider();
        var service = new ProbeService(services);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.Check("invoice.approve"));
    }
}
