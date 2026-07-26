using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Models;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultChatController 单元测试 - 覆盖 UserId 强制覆盖、参数传递、Result → ApiResult 转换
/// </summary>
public class DefaultChatControllerTests
{
    private readonly Mock<IChatService> _chatService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DefaultChatController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public DefaultChatControllerTests()
    {
        _controller = new DefaultChatController(_chatService.Object);

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
    // Chat (非流式)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Chat_AuthenticatedUser_OverridesUserIdWithCurrentUser()
    {
        var fakeUserId = Guid.NewGuid(); // 客户端伪造的 UserId
        var request = new ChatRequestDto { Message = "Hello", UserId = fakeUserId };
        var response = new ChatResponseDto { Content = "Hi there" };
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(response));

        var result = await _controller.Chat(request);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Content.ShouldBe("Hi there");
        // 验证传递给 service 的 request.UserId 已被覆盖为当前用户
        _chatService.Verify(s => s.ChatAsync(
            It.Is<ChatRequestDto>(r => r.UserId == _userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Chat_UnauthenticatedUser_KeepsOriginalUserId()
    {
        // 模拟未认证用户（CurrentUser 为 null）
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns((ICurrentUser?)null);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var originalUserId = Guid.NewGuid();
        var request = new ChatRequestDto { Message = "Hello", UserId = originalUserId };
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto()));

        await _controller.Chat(request);

        // CurrentUser 为 null 时，保留请求中原始的 UserId
        _chatService.Verify(s => s.ChatAsync(
            It.Is<ChatRequestDto>(r => r.UserId == originalUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Chat_ServiceReturnsSuccess_ReturnsSuccessApiResult()
    {
        var request = new ChatRequestDto { Message = "Hello" };
        var response = new ChatResponseDto { Content = "Response", Model = "gpt-4" };
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(response));

        var result = await _controller.Chat(request);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Model.ShouldBe("gpt-4");
    }

    [Fact]
    public async Task Chat_ServiceReturnsFailure_ReturnsFailedApiResult()
    {
        var request = new ChatRequestDto { Message = "Hello" };
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Failure("Quota exceeded", 429));

        var result = await _controller.Chat(request);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Quota exceeded");
        result.Code.ShouldBe(429);
    }

    [Fact]
    public async Task Chat_PassesCancellationTokenToService()
    {
        var request = new ChatRequestDto { Message = "Hello" };
        using var cts = new CancellationTokenSource();
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), cts.Token))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto()));

        await _controller.Chat(request, cts.Token);

        _chatService.Verify(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Chat_RequestWithAgentId_PassedToService()
    {
        var agentId = Guid.NewGuid();
        var request = new ChatRequestDto { Message = "Hello", AgentId = agentId };
        _chatService.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto()));

        await _controller.Chat(request);

        _chatService.Verify(s => s.ChatAsync(
            It.Is<ChatRequestDto>(r => r.AgentId == agentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
