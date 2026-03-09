
namespace Tnzi.Identity.Tests;

public class UserDetailServiceTests
{
    private readonly Mock<IRepository<UserDetail, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly UserDetailService _userDetailService;

    public UserDetailServiceTests()
    {
        _repositoryMock = new Mock<IRepository<UserDetail, Guid>>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _userDetailService = new UserDetailService(_repositoryMock.Object, _userManagerMock.Object, _serviceProviderMock.Object);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_WithNonExistentUser_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateUserDetailDto { FirstName = "Test" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userDetailService.CreateOrUpdateAsync(userId, dto);

        // Assert - 服务返回 Fail 而非抛异常
        Assert.False(result.Succeeded);
    }

}
