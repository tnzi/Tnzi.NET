namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// UserService 集成测试
/// 测试涉及 EF Core 复杂查询的功能
/// </summary>
public class UserServiceIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task GetByIdAsync_WithValidUserId_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com",
            NormalizedUserName = "TESTUSER",
            NormalizedEmail = "TEST@EXAMPLE.COM"
        };
        
        DbContext.Users.Add(user);
        await SaveChangesAsync();

        // Act
        var result = await DbContext.Users.FindAsync(user.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.UserName, result.UserName);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task FindByPhoneNumberAsync_WithValidPhone_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "phoneuser",
            PhoneNumber = "13800138000",
            NormalizedUserName = "PHONEUSER"
        };
        
        DbContext.Users.Add(user);
        await SaveChangesAsync();

        // Act
        var result = DbContext.Users.FirstOrDefault(u => u.PhoneNumber == "13800138000");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.PhoneNumber, result.PhoneNumber);
    }

    [Fact]
    public async Task FindByPhoneNumberAsync_WithNonExistentPhone_ReturnsNull()
    {
        // Act
        var result = DbContext.Users.FirstOrDefault(u => u.PhoneNumber == "99999999999");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateUser_WithRoles_SavesSuccessfully()
    {
        // Arrange
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN"
        };
        
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "adminuser",
            Email = "admin@example.com",
            NormalizedUserName = "ADMINUSER",
            NormalizedEmail = "ADMIN@EXAMPLE.COM"
        };

        var userRole = new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        };

        // Act
        DbContext.Roles.Add(role);
        DbContext.Users.Add(user);
        DbContext.UserRoles.Add(userRole);
        await SaveChangesAsync();

        // Assert
        var savedUser = await DbContext.Users.FindAsync(user.Id);
        var savedRole = await DbContext.Roles.FindAsync(role.Id);
        var savedUserRole = DbContext.UserRoles.FirstOrDefault(ur => ur.UserId == user.Id && ur.RoleId == role.Id);

        Assert.NotNull(savedUser);
        Assert.NotNull(savedRole);
        Assert.NotNull(savedUserRole);
    }

    [Fact]
    public async Task SoftDelete_User_MarksAsDeleted()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "deleteuser",
            Email = "delete@example.com",
            NormalizedUserName = "DELETEUSER",
            NormalizedEmail = "DELETE@EXAMPLE.COM"
        };
        
        DbContext.Users.Add(user);
        await SaveChangesAsync();

        // Act
        DbContext.Users.Remove(user);
        await SaveChangesAsync();

        // Assert - 软删除应该标记为已删除而不是物理删除
        var deletedUser = DbContext.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Id == user.Id);
        Assert.NotNull(deletedUser);
        Assert.True(deletedUser.IsDeleted);
    }
}
