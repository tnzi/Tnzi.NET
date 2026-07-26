using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Models;
using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultQuotaController 单元测试 - 覆盖用户 ID 获取、认证检查、Result → ApiResult 转换
/// </summary>
public class DefaultQuotaControllerTests
{
    private readonly Mock<IQuotaService> _quotaService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DefaultQuotaController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public DefaultQuotaControllerTests()
    {
        _controller = new DefaultQuotaController(_quotaService.Object);

        _currentUser.Setup(u => u.Id).Returns(_userId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    // -------------------------------------------------------------------------
    // GetMyQuota
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMyQuota_AuthenticatedUser_ReturnsQuota()
    {
        var quota = new UserQuotaDto
        {
            UserId = _userId,
            DailyTokenLimit = 100000,
            MonthlyTokenLimit = 3000000
        };
        _quotaService.Setup(s => s.GetQuotaAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserQuotaDto>.Success(quota));

        var result = await _controller.GetMyQuota();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.UserId.ShouldBe(_userId);
        result.Data.DailyTokenLimit.ShouldBe(100000);
    }

    [Fact]
    public async Task GetMyQuota_PassesCurrentUserIdToService()
    {
        _quotaService.Setup(s => s.GetQuotaAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserQuotaDto>.Success(new UserQuotaDto()));

        await _controller.GetMyQuota();

        _quotaService.Verify(s => s.GetQuotaAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyQuota_ServiceFails_ReturnsFailedResult()
    {
        _quotaService.Setup(s => s.GetQuotaAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserQuotaDto>.Failure("Quota not found", 404));

        var result = await _controller.GetMyQuota();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetMyQuota_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _quotaService.Setup(s => s.GetQuotaAsync(_userId, cts.Token))
            .ReturnsAsync(Result<UserQuotaDto>.Success(new UserQuotaDto()));

        await _controller.GetMyQuota(cts.Token);

        _quotaService.Verify(s => s.GetQuotaAsync(_userId, cts.Token), Times.Once);
    }

    // -------------------------------------------------------------------------
    // 未认证场景
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMyQuota_UserIdIsNull_ThrowsBusinessException()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);

        var ex = await Should.ThrowAsync<BusinessException>(() => _controller.GetMyQuota());
        ex.HttpStatusCode.ShouldBe(401);
    }

    [Fact]
    public void GetMyQuota_CurrentUserServiceNotRegistered_ThrowsInvalidOperationException()
    {
        // 模拟 ICurrentUser 未注册
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns((ICurrentUser?)null);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        Should.ThrowAsync<InvalidOperationException>(() => _controller.GetMyQuota());
    }
}
