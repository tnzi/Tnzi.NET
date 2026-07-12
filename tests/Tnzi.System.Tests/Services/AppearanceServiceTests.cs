namespace Tnzi.System.Tests.Services;

public class AppearanceServiceTests
{
    private readonly Mock<IRepository<Setting, Guid>> _repositoryMock = new();
    private readonly Mock<ISettingService> _settingServiceMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    public AppearanceServiceTests()
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);
    }

    private AppearanceService CreateService() => new(
        _serviceProviderMock.Object,
        _settingServiceMock.Object,
        _repositoryMock.Object);

    private static JsonElement ThemeObject(string json = """{"version":1,"admin":{"tabVisible":false},"ui":{"mode":"dark"}}""")
        => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task GetAdminTheme_Should_Return_Unset_When_No_Setting_Row()
    {
        _settingServiceMock
            .Setup(s => s.GetSettingAsync(AppearanceService.AdminThemeSettingKey, null))
            .ReturnsAsync(Result.Success<string?>(null));

        var result = await CreateService().GetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldBeNull();
        result.Data.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetAdminTheme_Should_Parse_Stored_Envelope()
    {
        var stored = """{"updatedAt":"2026-07-07T10:00:00Z","theme":{"version":1,"admin":{"tabVisible":false}}}""";
        _settingServiceMock
            .Setup(s => s.GetSettingAsync(AppearanceService.AdminThemeSettingKey, null))
            .ReturnsAsync(Result.Success<string?>(stored));

        var result = await CreateService().GetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldNotBeNull();
        result.Data.Theme!.Value.GetProperty("admin").GetProperty("tabVisible").GetBoolean().ShouldBeFalse();
        result.Data.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAdminTheme_Should_Treat_Corrupt_Row_As_Unset()
    {
        _settingServiceMock
            .Setup(s => s.GetSettingAsync(AppearanceService.AdminThemeSettingKey, null))
            .ReturnsAsync(Result.Success<string?>("{not-json"));

        var result = await CreateService().GetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAdminTheme_Should_Persist_Envelope_And_Echo_Theme()
    {
        string? persisted = null;
        _settingServiceMock
            .Setup(s => s.SetSettingAsync(AppearanceService.AdminThemeSettingKey, It.IsAny<string>(), It.IsAny<string?>(), AppearanceService.AppearanceSettingGroup))
            .Callback<string, string, string?, string?>((_, value, _, _) => persisted = value)
            .ReturnsAsync(Result.Success());

        var result = await CreateService().SaveAdminThemeAsync(new SaveAdminThemeDto { Theme = ThemeObject() });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldNotBeNull();
        result.Data.UpdatedAt.ShouldNotBeNull();
        persisted.ShouldNotBeNull();
        var envelope = JsonDocument.Parse(persisted!).RootElement;
        envelope.GetProperty("theme").GetProperty("ui").GetProperty("mode").GetString().ShouldBe("dark");
        envelope.GetProperty("updatedAt").GetDateTime().ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task SaveAdminTheme_Should_Reject_Non_Object_Theme()
    {
        var result = await CreateService().SaveAdminThemeAsync(new SaveAdminThemeDto { Theme = ThemeObject("[1,2,3]") });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _settingServiceMock.Verify(
            s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveAdminTheme_Should_Reject_Oversized_Theme()
    {
        var bigValue = new string('x', AppearanceService.MaxThemeJsonLength);
        var result = await CreateService().SaveAdminThemeAsync(new SaveAdminThemeDto
        {
            Theme = ThemeObject($$"""{"big":"{{bigValue}}"}"""),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ResetAdminTheme_Should_Be_Idempotent_When_Unset()
    {
        _repositoryMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Setting, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Setting?)null);

        var result = await CreateService().ResetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ResetAdminTheme_Should_Delete_Existing_Row()
    {
        var settingId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Setting, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Setting { Id = settingId, Key = AppearanceService.AdminThemeSettingKey });
        _settingServiceMock
            .Setup(s => s.DeleteSettingAsync(settingId))
            .ReturnsAsync(Result.Success());

        var result = await CreateService().ResetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(settingId), Times.Once);
    }
}
