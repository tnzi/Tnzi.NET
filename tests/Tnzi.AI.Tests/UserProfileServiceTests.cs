namespace Tnzi.AI.Tests;

/// <summary>
/// UserProfile 服务测试
/// </summary>
public class UserProfileServiceTests
{
    private readonly Mock<IRepository<UserProfile, Guid>> _profileRepo;
    private readonly IServiceProvider _serviceProvider;

    public UserProfileServiceTests()
    {
        _profileRepo = new Mock<IRepository<UserProfile, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private UserProfileService CreateService() => new(_serviceProvider, _profileRepo.Object);

    [Fact]
    public async Task GetOrCreateAsync_NewUser_CreatesProfile()
    {
        var userId = Guid.NewGuid();
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);
        _profileRepo.Setup(r => r.InsertAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateService().GetOrCreateAsync(userId);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, result.Data!.UserId);
    }

    [Fact]
    public async Task GetOrCreateAsync_ExistingUser_ReturnsExisting()
    {
        var userId = Guid.NewGuid();
        var existing = new UserProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = "Alice"
        };
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateService().GetOrCreateAsync(userId);

        Assert.True(result.Succeeded);
        Assert.Equal("Alice", result.Data!.DisplayName);
    }

    [Fact]
    public async Task UpdateAsync_ValidInput_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var existing = new UserProfile { Id = Guid.NewGuid(), UserId = userId };
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var input = new UpdateUserProfileDto
        {
            DisplayName = "Bob",
            PreferredLanguage = "zh-CN"
        };

        var result = await CreateService().UpdateAsync(userId, input);

        Assert.True(result.Succeeded);
    }
}
