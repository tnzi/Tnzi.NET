
namespace Tnzi.System.Tests.Services;

/// <summary>
/// MenuService 单元测试
/// </summary>
public class MenuServiceTests
{
    private readonly Mock<IRepository<Menu, Guid>> _menuRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ICache> _cacheMock;
    private readonly MenuService _service;

    public MenuServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _menuRepositoryMock = new Mock<IRepository<Menu, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _cacheMock = new Mock<ICache>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new MenuService(
            _serviceProviderMock.Object,
            _menuRepositoryMock.Object,
            _cacheMock.Object);
    }

    [Fact]
    public async Task GetMenusAsync_Should_Return_All_Menus()
    {
        // Arrange
        var menus = new List<Menu>
        {
            new Menu { Id = Guid.NewGuid(), Name = "Menu1", SortOrder = 1 },
            new Menu { Id = Guid.NewGuid(), Name = "Menu2", SortOrder = 2 }
        };

        _menuRepositoryMock.Setup(r => r.ToListAsync(default))
            .ReturnsAsync(menus);

        // Act
        var result = await _service.GetMenusAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetMenuByIdAsync_Should_Return_Menu_When_Exists()
    {
        // Arrange
        var menuId = Guid.NewGuid();
        var menu = new Menu { Id = menuId, Name = "Test Menu" };
        _menuRepositoryMock.Setup(r => r.GetAsync(menuId))
            .ReturnsAsync(menu);

        // Act
        var result = await _service.GetMenuByIdAsync(menuId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(menuId);
        result.Data.Name.ShouldBe("Test Menu");
    }

    [Fact]
    public async Task GetMenuByIdAsync_Should_Return_Fail_When_Not_Exists()
    {
        // Arrange
        var menuId = Guid.NewGuid();
        _menuRepositoryMock.Setup(r => r.GetAsync(menuId))
            .ReturnsAsync((Menu?)null);

        // Act
        var result = await _service.GetMenuByIdAsync(menuId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Data.ShouldBeNull();
    }

    [Fact]
    public async Task CreateMenuAsync_Should_Create_New_Menu()
    {
        // Arrange
        var input = new CreateMenuDto
        {
            Name = "New Menu",
            Path = "/test",
            SortOrder = 1
        };

        _menuRepositoryMock.Setup(r => r.InsertAsync(It.IsAny<Menu>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateMenuAsync(input);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Name.ShouldBe("New Menu");
        result.Data.Path.ShouldBe("/test");
        _menuRepositoryMock.Verify(r => r.InsertAsync(It.Is<Menu>(m => m.Name == "New Menu"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateMenuAsync_Should_Update_Existing_Menu()
    {
        // Arrange
        var menuId = Guid.NewGuid();
        var existingMenu = new Menu { Id = menuId, Name = "Old Name" };
        var input = new UpdateMenuDto
        {
            Name = "Updated Name",
            Path = "/updated"
        };

        _menuRepositoryMock.Setup(r => r.GetAsync(menuId))
            .ReturnsAsync(existingMenu);
        _menuRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Menu>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateMenuAsync(menuId, input);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(menuId);
        result.Data.Name.ShouldBe("Updated Name");
        _menuRepositoryMock.Verify(r => r.UpdateAsync(It.Is<Menu>(m => m.Name == "Updated Name"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUserMenuTreeAsync_Should_Return_Tree_Structure()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var menus = new List<Menu>
        {
            new Menu { Id = rootId, Name = "Root1", ParentId = null, SortOrder = 1, IsHidden = false },
            new Menu { Id = childId, Name = "Child1", ParentId = rootId, SortOrder = 1, IsHidden = false }
        };

        _cacheMock.Setup(x => x.GetAsync<List<Menu>>(It.Is<string>(s => s == "System:Menus:All"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(menus);

        // Act
        var result = await _service.GetUserMenuTreeAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
    }
}

/// <summary>
/// AccessLogService 单元测试
/// </summary>
public class AccessLogServiceTests
{
    private readonly Mock<IRepository<AccessLog, Guid>> _accessLogRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IAccessLogSender> _accessLogSenderMock;
    private readonly AccessLogService _service;

    public AccessLogServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _accessLogRepositoryMock = new Mock<IRepository<AccessLog, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _accessLogSenderMock = new Mock<IAccessLogSender>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _accessLogSenderMock.Setup(x => x.SendAsync(It.IsAny<AccessLogDto>())).Returns(Task.CompletedTask);

        _service = new AccessLogService(
            _serviceProviderMock.Object,
            _accessLogRepositoryMock.Object,
            _accessLogSenderMock.Object);
    }

    [Fact]
    public async Task LogAccessAsync_Should_Send_To_Background_Queue()
    {
        // Arrange
        var logDto = new AccessLogDto
        {
            UserId = Guid.NewGuid(),
            UserName = "TestUser",
            Path = "/api/test",
            Method = "GET",
            IpAddress = "127.0.0.1",
            StatusCode = 200,
            ResponseTime = 100
        };

        // Act
        var result = await _service.LogAccessAsync(logDto);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _accessLogSenderMock.Verify(s => s.SendAsync(
            It.Is<AccessLogDto>(l => l.UserName == "TestUser" && l.Path == "/api/test")), Times.Once);
    }
}

/// <summary>
/// SettingService 单元测试
/// </summary>
public class SettingServiceTests
{
    private readonly Mock<IRepository<Setting, Guid>> _settingRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IOptionsMonitor<ApplicationOptions>> _applicationOptionsMock;
    private readonly Mock<ICache> _cacheMock;
    private readonly SettingService _service;

    public SettingServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _settingRepositoryMock = new Mock<IRepository<Setting, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _applicationOptionsMock = new Mock<IOptionsMonitor<ApplicationOptions>>();
        _applicationOptionsMock.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions { AppName = "TestApp", SiteName = "TestSite" });
        _cacheMock = new Mock<ICache>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        var encryptionOptions = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions());

        _service = new SettingService(
            _serviceProviderMock.Object,
            _settingRepositoryMock.Object,
            _applicationOptionsMock.Object,
            encryptionOptions,
            _cacheMock.Object,
            Enumerable.Empty<ISettingProvider>());
    }

    [Fact]
    public void GetApplicationOptions_Should_Return_Options()
    {
        // Act
        var result = _service.GetApplicationOptions();

        // Assert
        result.ShouldNotBeNull();
        result.AppName.ShouldBe("TestApp");
        result.SiteName.ShouldBe("TestSite");
    }

    [Fact]
    public async Task GetAppNameAsync_Should_Return_Value_From_IOptionsMonitor_Without_Setting_Row()
    {
        // Arrange: IOptionsMonitor returns "MyApp"; no App.AppName row in Setting table
        _applicationOptionsMock.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions { AppName = "MyApp", SiteName = "MySite" });

        // No cache hit
        _cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        // Act
        var result = await _service.GetAppNameAsync();

        // Assert: value comes directly from IOptionsMonitor, not the Setting table
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("MyApp");

        // The repository must NOT be queried — the legacy App.AppName table read path is gone
        _settingRepositoryMock.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetSiteNameAsync_Should_Return_Value_From_IOptionsMonitor_Without_Setting_Row()
    {
        // Arrange: IOptionsMonitor returns "MySite"; no App.SiteName row in Setting table
        _applicationOptionsMock.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions { AppName = "MyApp", SiteName = "MySite" });

        // No cache hit
        _cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        // Act
        var result = await _service.GetSiteNameAsync();

        // Assert: value comes directly from IOptionsMonitor, not the Setting table
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("MySite");

        // The repository must NOT be queried — the legacy App.SiteName table read path is gone
        _settingRepositoryMock.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetAppNameAsync_Should_Reflect_IOptionsMonitor_Changes()
    {
        // Simulate hot-reload: CurrentValue changes between calls
        _applicationOptionsMock.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions { AppName = "UpdatedApp", SiteName = "UpdatedSite" });

        var result = await _service.GetAppNameAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("UpdatedApp");
    }
}
