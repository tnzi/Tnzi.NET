namespace Tnzi.System.Tests.Services;

public class AppearanceServiceTests
{
    private const string AdminKey = AppearanceService.ThemeSettingKeyPrefix + IAppearanceService.AdminScope;
    private const string ChatKey = AppearanceService.ThemeSettingKeyPrefix + "chat";

    private readonly Mock<IRepository<Setting, Guid>> _repositoryMock = new();
    private readonly Mock<ISettingService> _settingServiceMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    public AppearanceServiceTests()
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        // Unstubbed keys read as "no row" rather than throwing, so a test only
        // has to set up the key it is actually about.
        _settingServiceMock
            .Setup(s => s.GetSettingAsync(It.IsAny<string>(), null))
            .ReturnsAsync(Result.Success<string?>(null));
    }

    /// Returned as the interface: the `*AdminTheme*` overloads are default
    /// interface methods, so they exist only through `IAppearanceService`.
    private IAppearanceService CreateService() => new AppearanceService(
        _serviceProviderMock.Object,
        _settingServiceMock.Object,
        _repositoryMock.Object);

    private static JsonElement ThemeObject(string json = """{"version":1,"admin":{"tabVisible":false},"ui":{"mode":"dark"}}""")
        => JsonDocument.Parse(json).RootElement.Clone();

    private void StoredAt(string key, string? value) => _settingServiceMock
        .Setup(s => s.GetSettingAsync(key, null))
        .ReturnsAsync(Result.Success<string?>(value));

    [Fact]
    public async Task GetTheme_Should_Return_Unset_When_No_Setting_Row()
    {
        var result = await CreateService().GetThemeAsync(IAppearanceService.AdminScope);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldBeNull();
        result.Data.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetTheme_Should_Parse_Stored_Envelope()
    {
        StoredAt(AdminKey, """{"updatedAt":"2026-07-07T10:00:00Z","theme":{"version":1,"admin":{"tabVisible":false}}}""");

        var result = await CreateService().GetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldNotBeNull();
        result.Data.Theme!.Value.GetProperty("admin").GetProperty("tabVisible").GetBoolean().ShouldBeFalse();
        result.Data.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetTheme_Should_Treat_Corrupt_Row_As_Unset()
    {
        StoredAt(AdminKey, "{not-json");

        var result = await CreateService().GetThemeAsync(IAppearanceService.AdminScope);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldBeNull();
    }

    [Fact]
    public async Task GetTheme_Should_Keep_Scopes_Isolated()
    {
        StoredAt(AdminKey, """{"theme":{"who":"admin"}}""");
        StoredAt(ChatKey, """{"theme":{"who":"chat"}}""");

        var service = CreateService();

        (await service.GetThemeAsync("admin")).Data!.Theme!.Value.GetProperty("who").GetString().ShouldBe("admin");
        (await service.GetThemeAsync("chat")).Data!.Theme!.Value.GetProperty("who").GetString().ShouldBe("chat");
    }

    /// A deployment that configured its admin theme before scoping existed must
    /// not look like it was reset after the upgrade.
    [Fact]
    public async Task GetTheme_Should_Fall_Back_To_PreScope_Key_For_Admin()
    {
        StoredAt(AppearanceService.LegacyAdminThemeSettingKey, """{"theme":{"legacy":true}}""");

        var result = await CreateService().GetThemeAsync(IAppearanceService.AdminScope);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme!.Value.GetProperty("legacy").GetBoolean().ShouldBeTrue();
    }

    /// The fallback is admin-only: a new scope reading a stale admin theme
    /// would silently dress the wrong product.
    [Fact]
    public async Task GetTheme_Should_Not_Fall_Back_For_Other_Scopes()
    {
        StoredAt(AppearanceService.LegacyAdminThemeSettingKey, """{"theme":{"legacy":true}}""");

        var result = await CreateService().GetThemeAsync("chat");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9lives")]
    [InlineData("has space")]
    [InlineData("has:colon")]
    [InlineData("way-too-long-a-scope-name-that-keeps-going-and-going")]
    public async Task GetTheme_Should_Reject_Malformed_Scope(string scope)
    {
        // The scope becomes part of a Setting key, so an unvalidated one lets a
        // caller address arbitrary rows.
        var result = await CreateService().GetThemeAsync(scope);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task SaveTheme_Should_Persist_Envelope_And_Echo_Theme()
    {
        string? persisted = null;
        _settingServiceMock
            .Setup(s => s.SetSettingAsync(ChatKey, It.IsAny<string>(), It.IsAny<string?>(), AppearanceService.AppearanceSettingGroup))
            .Callback<string, string, string?, string?>((_, value, _, _) => persisted = value)
            .ReturnsAsync(Result.Success());

        var result = await CreateService().SaveThemeAsync("chat", new SaveThemeSnapshotDto { Theme = ThemeObject() });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Theme.ShouldNotBeNull();
        result.Data.UpdatedAt.ShouldNotBeNull();
        persisted.ShouldNotBeNull();
        var envelope = JsonDocument.Parse(persisted!).RootElement;
        envelope.GetProperty("theme").GetProperty("ui").GetProperty("mode").GetString().ShouldBe("dark");
        envelope.GetProperty("updatedAt").GetDateTime().ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }

    /// Writes always target the scoped key - that is what makes the pre-scope
    /// fallback a read-only migration path rather than a permanent fork.
    [Fact]
    public async Task SaveTheme_Should_Write_The_Scoped_Key_Not_The_Legacy_One()
    {
        _settingServiceMock
            .Setup(s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());

        await CreateService().SaveAdminThemeAsync(new SaveThemeSnapshotDto { Theme = ThemeObject() });

        _settingServiceMock.Verify(
            s => s.SetSettingAsync(AdminKey, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
        _settingServiceMock.Verify(
            s => s.SetSettingAsync(AppearanceService.LegacyAdminThemeSettingKey, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveTheme_Should_Reject_Non_Object_Theme()
    {
        var result = await CreateService().SaveAdminThemeAsync(new SaveThemeSnapshotDto { Theme = ThemeObject("[1,2,3]") });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _settingServiceMock.Verify(
            s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveTheme_Should_Reject_Oversized_Theme()
    {
        var bigValue = new string('x', AppearanceService.MaxThemeJsonLength);
        var result = await CreateService().SaveAdminThemeAsync(new SaveThemeSnapshotDto
        {
            Theme = ThemeObject($$"""{"big":"{{bigValue}}"}"""),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ResetTheme_Should_Be_Idempotent_When_Unset()
    {
        _repositoryMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Setting, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Setting?)null);

        var result = await CreateService().ResetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ResetTheme_Should_Delete_Existing_Row()
    {
        var settingId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Setting, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Setting { Id = settingId, Key = ChatKey });
        _settingServiceMock
            .Setup(s => s.DeleteSettingAsync(settingId))
            .ReturnsAsync(Result.Success());

        var result = await CreateService().ResetThemeAsync("chat");

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(settingId), Times.Once);
    }

    /// Resetting `admin` must clear the pre-scope row as well; leaving it would
    /// make the reset a no-op, because the read path falls back to it.
    [Fact]
    public async Task ResetTheme_Should_Also_Clear_The_PreScope_Row_For_Admin()
    {
        var scopedId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        var deleted = new List<Guid>();

        _repositoryMock
            .SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Setting, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Setting { Id = scopedId, Key = AdminKey })
            .ReturnsAsync(new Setting { Id = legacyId, Key = AppearanceService.LegacyAdminThemeSettingKey });
        _settingServiceMock
            .Setup(s => s.DeleteSettingAsync(It.IsAny<Guid>()))
            .Callback<Guid>(deleted.Add)
            .ReturnsAsync(Result.Success());

        var result = await CreateService().ResetAdminThemeAsync();

        result.Succeeded.ShouldBeTrue();
        deleted.ShouldBe([scopedId, legacyId]);
    }

    [Fact]
    public async Task ResetTheme_Should_Reject_Malformed_Scope()
    {
        var result = await CreateService().ResetThemeAsync("Bad Scope");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(It.IsAny<Guid>()), Times.Never);
    }
}
