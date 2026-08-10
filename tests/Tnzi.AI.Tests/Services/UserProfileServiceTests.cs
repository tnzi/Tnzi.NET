namespace Tnzi.AI.Tests.Services;

/// <summary>
/// UserProfileService 单元测试。
///
/// 重点是「清空字段」这条路径：<see cref="UpdateUserProfileDto.Content"/> 可空，
/// 而 <see cref="UserProfile.Content"/> 对应的是 NOT NULL 列，Mapster 会把 null
/// 原样赋过去 —— 于是任何清空正文的客户端都会撞 500（DbUpdateException）。
/// 这一条真实发生过（2026-08-05 浏览器实测），故留回归测试。
/// </summary>
public class UserProfileServiceTests
{
    private readonly Mock<IRepository<UserProfile, Guid>> _repository;
    private readonly IServiceProvider _serviceProvider;

    public UserProfileServiceTests()
    {
        _repository = new Mock<IRepository<UserProfile, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private UserProfileService CreateService() => new(_serviceProvider, _repository.Object);

    private void SetupExisting(UserProfile? existing)
    {
        _repository
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repository
            .Setup(r => r.InsertAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task UpdateAsync_NullContent_OnExistingProfile_StoresEmptyNotNull()
    {
        // Arrange - the user cleared the box; the DTO permits null.
        var userId = Guid.NewGuid();
        var existing = new UserProfile { UserId = userId, Content = "old content" };
        SetupExisting(existing);

        // Act
        var result = await CreateService().UpdateAsync(userId, new UpdateUserProfileDto { Content = null });

        // Assert - never null, or the NOT NULL column rejects the write.
        result.Succeeded.ShouldBeTrue();
        existing.Content.ShouldNotBeNull();
        existing.Content.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_NullContent_OnNewProfile_StoresEmptyNotNull()
    {
        // The insert path maps the same DTO onto a fresh entity and must guard too.
        var userId = Guid.NewGuid();
        SetupExisting(null);

        UserProfile? inserted = null;
        _repository
            .Setup(r => r.InsertAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .Callback<UserProfile, CancellationToken>((p, _) => inserted = p)
            .Returns(Task.CompletedTask);

        var result = await CreateService().UpdateAsync(userId, new UpdateUserProfileDto { Content = null });

        result.Succeeded.ShouldBeTrue();
        inserted.ShouldNotBeNull();
        inserted!.Content.ShouldNotBeNull();
        inserted.Content.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_NullableColumns_TakeNullAsCleared()
    {
        // The other three ARE nullable columns, so null means cleared there and
        // must not be coerced to "" - that would store a blank preferred
        // language rather than none at all.
        var userId = Guid.NewGuid();
        var existing = new UserProfile
        {
            UserId = userId,
            DisplayName = "Ada",
            Role = "Engineer",
            PreferredLanguage = "en",
            Content = "keep",
        };
        SetupExisting(existing);

        await CreateService().UpdateAsync(userId, new UpdateUserProfileDto
        {
            DisplayName = null,
            Role = null,
            PreferredLanguage = null,
            Content = "keep",
        });

        existing.DisplayName.ShouldBeNull();
        existing.Role.ShouldBeNull();
        existing.PreferredLanguage.ShouldBeNull();
        existing.Content.ShouldBe("keep");
    }

    [Fact]
    public async Task UpdateAsync_WritesSuppliedValues()
    {
        var userId = Guid.NewGuid();
        var existing = new UserProfile { UserId = userId, Content = "old" };
        SetupExisting(existing);

        await CreateService().UpdateAsync(userId, new UpdateUserProfileDto
        {
            DisplayName = "Ada",
            Content = "Prefers concise answers.",
        });

        existing.DisplayName.ShouldBe("Ada");
        existing.Content.ShouldBe("Prefers concise answers.");
    }
}
