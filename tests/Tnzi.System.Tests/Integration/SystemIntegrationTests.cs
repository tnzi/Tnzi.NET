
namespace Tnzi.System.Tests.Integration;

/// <summary>
/// System 模块集成测试
/// </summary>
public class SystemIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Should_Create_And_Query_Menu()
    {
        // Arrange
        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Dashboard",
            Path = "/dashboard",
            Icon = "dashboard",
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Menus.AddAsync(menu);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedMenu = await DbContext.Menus.FirstOrDefaultAsync(m => m.Name == "Dashboard");
        savedMenu.ShouldNotBeNull();
        savedMenu!.Path.ShouldBe("/dashboard");
        savedMenu.Icon.ShouldBe("dashboard");
    }

    [Fact]
    public async Task Should_Create_Menu_Hierarchy()
    {
        // Arrange
        var parentMenu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "System",
            Path = "/system",
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Menus.AddAsync(parentMenu);
        await DbContext.SaveChangesAsync();

        var childMenu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Users",
            Path = "/system/users",
            ParentId = parentMenu.Id,
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Menus.AddAsync(childMenu);
        await DbContext.SaveChangesAsync();

        // Act
        var children = await DbContext.Menus
            .Where(m => m.ParentId == parentMenu.Id)
            .ToListAsync();

        // Assert
        children.Count.ShouldBe(1);
        children.First().Name.ShouldBe("Users");
    }

    [Fact]
    public async Task Should_Update_Menu()
    {
        // Arrange
        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            Path = "/old",
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Menus.AddAsync(menu);
        await DbContext.SaveChangesAsync();

        // Act
        menu.Name = "New Name";
        menu.Path = "/new";
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedMenu = await DbContext.Menus.FindAsync(menu.Id);
        updatedMenu.ShouldNotBeNull();
        updatedMenu!.Name.ShouldBe("New Name");
        updatedMenu.Path.ShouldBe("/new");
    }

    [Fact]
    public async Task Should_Delete_Menu()
    {
        // Arrange
        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Delete Test",
            Path = "/delete",
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Menus.AddAsync(menu);
        await DbContext.SaveChangesAsync();

        // Act
        DbContext.Menus.Remove(menu);
        await DbContext.SaveChangesAsync();

        // Assert - Menu 支持软删除
        var deletedMenu = await DbContext.Menus
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == menu.Id);
        
        deletedMenu.ShouldNotBeNull();
        deletedMenu!.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Create_AccessLog()
    {
        // Arrange
        var accessLog = new AccessLog
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserName = "testuser",
            Path = "/api/test",
            Method = "GET",
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            StatusCode = 200,
            ResponseTime = 150,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.AccessLogs.AddAsync(accessLog);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedLog = await DbContext.AccessLogs.FirstOrDefaultAsync(l => l.UserName == "testuser");
        savedLog.ShouldNotBeNull();
        savedLog!.IpAddress.ShouldBe("192.168.1.1");
        savedLog.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task Should_Query_AccessLogs_By_User()
    {
        // Arrange
        var userId = Guid.NewGuid();
        for (int i = 1; i <= 5; i++)
        {
            await DbContext.AccessLogs.AddAsync(new AccessLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserName = "testuser",
                Path = $"/api/test{i}",
                Method = "GET",
                IpAddress = "192.168.1.1",
                StatusCode = 200,
                ResponseTime = 100 + i,
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var userLogs = await DbContext.AccessLogs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreationTime)
            .ToListAsync();

        // Assert
        userLogs.Count.ShouldBe(5);
        userLogs.First().Path.ShouldBe("/api/test1");
    }

    [Fact]
    public async Task Should_Query_Menus_With_Paging()
    {
        // Arrange
        for (int i = 1; i <= 20; i++)
        {
            await DbContext.Menus.AddAsync(new Menu
            {
                Id = Guid.NewGuid(),
                Name = $"Menu {i}",
                Path = $"/menu{i}",
                SortOrder = i,
                IsHidden = false,
                CreationTime = DateTime.UtcNow
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var page1 = await DbContext.Menus
            .OrderBy(m => m.SortOrder)
            .Skip(0)
            .Take(10)
            .ToListAsync();

        var page2 = await DbContext.Menus
            .OrderBy(m => m.SortOrder)
            .Skip(10)
            .Take(10)
            .ToListAsync();

        // Assert
        page1.Count.ShouldBe(10);
        page2.Count.ShouldBe(10);
        page1.First().Name.ShouldBe("Menu 1");
        page2.First().Name.ShouldBe("Menu 11");
    }

    [Fact]
    public async Task Should_Query_Visible_Menus_Only()
    {
        // Arrange
        await DbContext.Menus.AddAsync(new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Visible Menu",
            Path = "/visible",
            SortOrder = 1,
            IsHidden = false,
            CreationTime = DateTime.UtcNow
        });

        await DbContext.Menus.AddAsync(new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Hidden Menu",
            Path = "/hidden",
            SortOrder = 2,
            IsHidden = true,
            CreationTime = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();

        // Act
        var visibleMenus = await DbContext.Menus
            .Where(m => !m.IsHidden)
            .ToListAsync();

        // Assert
        visibleMenus.Count.ShouldBe(1);
        visibleMenus.First().Name.ShouldBe("Visible Menu");
    }
}