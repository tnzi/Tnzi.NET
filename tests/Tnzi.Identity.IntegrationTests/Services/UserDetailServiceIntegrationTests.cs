using Tnzi.Identity.Services;
using Tnzi.Identity.Dtos;

namespace Tnzi.Identity.IntegrationTests.Services;

public class UserDetailServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserDetailService _service;

    public UserDetailServiceIntegrationTests()
    {
        _service = new UserDetailService(CreateRepository<UserDetail>(), UserManager, ServiceProvider);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithExistingDetail_ReturnsUserDetailDto()
    {
        var user = await CreateUserAsync();
        DbContext.UserDetails.Add(new UserDetail { Id = Guid.NewGuid(), UserId = user.Id, Nickname = "Tester" });
        await SaveChangesAsync();

        var result = await _service.GetByUserIdAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Tester", result.Data!.Nickname);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithNonExistingDetail_ReturnsNull()
    {
        var user = await CreateUserAsync();

        var result = await _service.GetByUserIdAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_WithNewDetail_CreatesDetail()
    {
        var user = await CreateUserAsync();

        var result = await _service.CreateOrUpdateAsync(user.Id, new CreateUserDetailDto
        {
            Nickname = "Created",
            Bio = "New detail"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Created", DbContext.UserDetails.Single().Nickname);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_WithExistingDetail_UpdatesDetail()
    {
        var user = await CreateUserAsync();
        DbContext.UserDetails.Add(new UserDetail { Id = Guid.NewGuid(), UserId = user.Id, Nickname = "Old" });
        await SaveChangesAsync();

        var result = await _service.CreateOrUpdateAsync(user.Id, new CreateUserDetailDto
        {
            Nickname = "Updated",
            Bio = "Changed"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", DbContext.UserDetails.Single().Nickname);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingDetail_DeletesDetail()
    {
        var user = await CreateUserAsync();
        DbContext.UserDetails.Add(new UserDetail { Id = Guid.NewGuid(), UserId = user.Id, Nickname = "DeleteMe" });
        await SaveChangesAsync();

        var result = await _service.DeleteAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.True(DbContext.UserDetails.IgnoreQueryFilters().Single().IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingDetail_DoesNothing()
    {
        var user = await CreateUserAsync();

        var result = await _service.DeleteAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }
}
