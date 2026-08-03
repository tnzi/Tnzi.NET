
namespace Tnzi.System.Tests.Services;

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
            Enumerable.Empty<ISettingProvider>(),
            Enumerable.Empty<ISettingDefinitionProvider>());
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

        // The repository must NOT be queried - the legacy App.AppName table read path is gone
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

        // The repository must NOT be queried - the legacy App.SiteName table read path is gone
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

    private sealed class ManagedKeyProvider : ISettingDefinitionProvider
    {
        public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
        [
            new SettingDefinitionGroup
            {
                Key = "demo",
                ModuleName = "Demo",
                DisplayName = "Demo",
                Fields = [new SettingFieldDefinition { Key = "Demo:Managed", Label = "M", Type = SettingFieldType.Int }],
            },
        ];
    }

    private SettingService CreateServiceWithManagedKeys()
    {
        var encryptionOptions = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions());
        return new SettingService(
            _serviceProviderMock.Object,
            _settingRepositoryMock.Object,
            _applicationOptionsMock.Object,
            encryptionOptions,
            _cacheMock.Object,
            Enumerable.Empty<ISettingProvider>(),
            new ISettingDefinitionProvider[] { new ManagedKeyProvider() });
    }

    [Fact]
    public async Task CreateSetting_Should_Reject_Global_Key_Managed_By_Settings_Center()
    {
        // 后门收口回归：原始 CRUD 零 schema 校验，命中配置中心定义的 Global 键必须 400
        //（否则非法值经 SettingConfigurationProvider 流入 IConfiguration，重绑定即抛异常）。
        var service = CreateServiceWithManagedKeys();

        var result = await service.CreateSettingAsync(new CreateSettingDto
        {
            Key = "Demo:Managed",
            Value = "not-an-int",
            Scope = SettingScope.Global,
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("settings center");
        _settingRepositoryMock.Verify(r => r.InsertAsync(It.IsAny<Setting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSetting_Should_Reject_Global_Key_Managed_By_Settings_Center()
    {
        var service = CreateServiceWithManagedKeys();
        var row = new Setting { Id = Guid.NewGuid(), Key = "Demo:Managed", Value = "1", Scope = SettingScope.Global };
        _settingRepositoryMock.Setup(r => r.GetAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await service.UpdateSettingAsync(row.Id, new UpdateSettingDto { Value = "abc" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _settingRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Setting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSetting_Should_Allow_NonGlobal_Scope_And_Unmanaged_Keys()
    {
        // 仅 Global 作用域受管（Tenant/User 行不进 IConfiguration）；未受管的自定义键不受影响。
        // 哨兵异常证明「后门拦截层已通过、到达唯一性检查」（内存 IQueryable 跑不了 EF 异步算子，
        // 无法走完完整创建路径 - 这里只锁拦截边界）。
        var service = CreateServiceWithManagedKeys();
        _settingRepositoryMock
            .Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Throws(new InvalidOperationException("sentinel: reached uniqueness check"));

        var tenantScoped = await Should.ThrowAsync<InvalidOperationException>(() => service.CreateSettingAsync(new CreateSettingDto
        {
            Key = "Demo:Managed",
            Value = "42",
            Scope = SettingScope.Tenant,
            ScopeId = "t1",
        }));
        tenantScoped.Message.ShouldContain("sentinel");

        var unmanaged = await Should.ThrowAsync<InvalidOperationException>(() => service.CreateSettingAsync(new CreateSettingDto
        {
            Key = "Custom.Key",
            Value = "v",
            Scope = SettingScope.Global,
        }));
        unmanaged.Message.ShouldContain("sentinel");
    }
}
