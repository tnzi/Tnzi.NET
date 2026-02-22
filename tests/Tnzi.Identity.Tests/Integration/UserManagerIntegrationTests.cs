
namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// Identity DbContext 集成测试
/// </summary>
public class IdentityDbContextIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Should_Create_And_Query_User()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com",
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Users.AddAsync(user);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedUser = await DbContext.Users.FirstOrDefaultAsync(u => u.UserName == "testuser");
        savedUser.ShouldNotBeNull();
        savedUser!.Email.ShouldBe("test@example.com");
    }

    [Fact]
    public async Task Should_Update_User_Properties()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "updateuser",
            Email = "old@example.com",
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Users.AddAsync(user);
        await DbContext.SaveChangesAsync();

        // Act
        user.Email = "new@example.com";
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedUser = await DbContext.Users.FirstOrDefaultAsync(u => u.UserName == "updateuser");
        updatedUser.ShouldNotBeNull();
        updatedUser!.Email.ShouldBe("new@example.com");
    }

    [Fact]
    public async Task Should_Delete_User()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "deleteuser",
            Email = "delete@example.com",
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Users.AddAsync(user);
        await DbContext.SaveChangesAsync();

        // Act
        DbContext.Users.Remove(user);
        await DbContext.SaveChangesAsync();

        // Assert
        var deletedUser = await DbContext.Users.FirstOrDefaultAsync(u => u.UserName == "deleteuser");
        deletedUser.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Create_Organization_Hierarchy()
    {
        // Arrange
        var rootOrg = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Root Organization",
            Code = "ROOT",
            CreationTime = DateTime.UtcNow
        };

        var childOrg = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Child Organization",
            Code = "CHILD",
            ParentId = rootOrg.Id,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Organizations.AddAsync(rootOrg);
        await DbContext.Organizations.AddAsync(childOrg);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedRoot = await DbContext.Organizations
            .Include(o => o.Children)
            .FirstOrDefaultAsync(o => o.Code == "ROOT");
        
        savedRoot.ShouldNotBeNull();
        savedRoot!.Children.ShouldNotBeNull();
        savedRoot.Children.Count.ShouldBe(1);
        savedRoot.Children.First().Name.ShouldBe("Child Organization");
    }

    [Fact]
    public async Task Should_Create_LoginLog()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "loginuser",
            Email = "login@example.com",
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Users.AddAsync(user);
        await DbContext.SaveChangesAsync();

        var loginLog = new LoginLog
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            UserName = user.UserName,
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            Status = LoginStatus.Success,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.LoginLogs.AddAsync(loginLog);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedLog = await DbContext.LoginLogs
            .FirstOrDefaultAsync(l => l.UserId == user.Id);
        
        savedLog.ShouldNotBeNull();
        savedLog!.UserName.ShouldBe("loginuser");
        savedLog.IpAddress.ShouldBe("192.168.1.1");
        savedLog.Status.ShouldBe(LoginStatus.Success);
    }

    [Fact]
    public async Task Should_Query_Users_With_Paging()
    {
        // Arrange
        for (int i = 1; i <= 25; i++)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = $"user{i}",
                Email = $"user{i}@example.com",
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            };
            await DbContext.Users.AddAsync(user);
        }
        await DbContext.SaveChangesAsync();

        // Act
        var page1 = await DbContext.Users
            .OrderByDescending(u => u.CreationTime)
            .Skip(0)
            .Take(10)
            .ToListAsync();

        var page2 = await DbContext.Users
            .OrderByDescending(u => u.CreationTime)
            .Skip(10)
            .Take(10)
            .ToListAsync();

        // Assert
        page1.Count.ShouldBe(10);
        page2.Count.ShouldBe(10);
        page1.First().UserName.ShouldBe("user1");
        page2.First().UserName.ShouldBe("user11");
    }
}