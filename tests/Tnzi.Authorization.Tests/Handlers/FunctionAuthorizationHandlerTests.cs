using IFunctionAuthorizationService = Tnzi.Authorization.Services.IFunctionAuthorizationService;

namespace Tnzi.Authorization.Tests.Handlers;

/// <summary>
/// FunctionAuthorizationHandler 单元测试
/// </summary>
public class FunctionAuthorizationHandlerTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IFunctionAuthorizationService> _authorizationServiceMock;

    public FunctionAuthorizationHandlerTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _currentUserMock = new Mock<ICurrentUser>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _authorizationServiceMock = new Mock<IFunctionAuthorizationService>();

        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IHttpContextAccessor)))
            .Returns(_httpContextAccessorMock.Object);
        
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IFunctionAuthorizationService)))
            .Returns(_authorizationServiceMock.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_WithUnauthenticatedUser_Fails()
    {
        // Arrange
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement();
        
        // 未认证的用户
        var claims = new ClaimsPrincipal();
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithNoHttpContext_Fails()
    {
        // Arrange
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement();
        
        // 已认证但无 HttpContext
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "Test");
        var claims = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithNoPermissionName_Succeeds()
    {
        // Arrange
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement(); // 无权限名称
        
        // 已认证用户
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "Test");
        var claims = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        // 设置 HttpContext
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _currentUserMock.Setup(x => x.Id).Returns(Guid.NewGuid());

        // Act
        await handler.HandleAsync(context);

        // Assert
        // 无权限名称时，只验证已登录即可
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithNullUserId_Fails()
    {
        // Arrange
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement("test.permission");
        
        // 已认证用户
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "Test");
        var claims = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        // 设置 HttpContext
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // 用户ID为null
        _currentUserMock.Setup(x => x.Id).Returns((Guid?)null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithValidPermission_Succeeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionName = "test.permission";
        
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement(permissionName);
        
        // 已认证用户
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "Test");
        var claims = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        // 设置 HttpContext
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _currentUserMock.Setup(x => x.Id).Returns(userId);
        _authorizationServiceMock
            .Setup(x => x.CheckPermissionAsync(userId, permissionName))
            .ReturnsAsync(true);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithInvalidPermission_Fails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionName = "test.permission";
        
        var handler = new FunctionAuthorizationHandler(_serviceProviderMock.Object, _currentUserMock.Object);
        var requirement = new FunctionAuthorizationRequirement(permissionName);
        
        // 已认证用户
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "Test");
        var claims = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            claims, 
            null);

        // 设置 HttpContext
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _currentUserMock.Setup(x => x.Id).Returns(userId);
        _authorizationServiceMock
            .Setup(x => x.CheckPermissionAsync(userId, permissionName))
            .ReturnsAsync(false);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }
}