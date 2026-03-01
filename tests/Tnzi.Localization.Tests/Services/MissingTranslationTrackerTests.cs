namespace Tnzi.Localization.Tests.Services;

/// <summary>
/// Tests for MissingTranslationTracker — tracking, querying, export, and clearing missing translations
/// </summary>
public class MissingTranslationTrackerTests
{
    private readonly MissingTranslationTracker _tracker;
    private readonly Mock<ILogger<MissingTranslationTracker>> _loggerMock;

    public MissingTranslationTrackerTests()
    {
        _loggerMock = new Mock<ILogger<MissingTranslationTracker>>();
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.Setup(e => e.EnvironmentName).Returns("Development");
        _tracker = new MissingTranslationTracker(_loggerMock.Object, environmentMock.Object);
    }

    [Fact]
    public async Task TrackMissing_SingleKey_RecordedCorrectly()
    {
        // Act
        _tracker.TrackMissing("en", "Auth.Login");

        // Assert
        var missing = await _tracker.GetMissingKeysAsync();
        Assert.Single(missing);
        Assert.Equal("en", missing[0].Culture);
        Assert.Equal("Auth.Login", missing[0].Key);
        Assert.Equal(1, missing[0].AccessCount);
    }

    [Fact]
    public async Task TrackMissing_SameKeyMultipleTimes_IncrementsCount()
    {
        // Act
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("en", "Auth.Login");

        // Assert
        var missing = await _tracker.GetMissingKeysAsync();
        Assert.Single(missing);
        Assert.Equal(3, missing[0].AccessCount);
    }

    [Fact]
    public async Task TrackMissing_DifferentKeys_TrackedSeparately()
    {
        // Act
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("en", "Auth.Logout");
        _tracker.TrackMissing("zh-CN", "Auth.Login");

        // Assert
        var missing = await _tracker.GetMissingKeysAsync();
        Assert.Equal(3, missing.Count);
    }

    [Fact]
    public async Task GetMissingKeysAsync_FilterByCulture_ReturnsOnlyMatchingCulture()
    {
        // Arrange
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("zh-CN", "Auth.Login");
        _tracker.TrackMissing("zh-CN", "Auth.Logout");

        // Act
        var missing = await _tracker.GetMissingKeysAsync("zh-CN");

        // Assert
        Assert.Equal(2, missing.Count);
        Assert.All(missing, m => Assert.Equal("zh-CN", m.Culture));
    }

    [Fact]
    public async Task GetMissingKeysAsync_NoFilter_ReturnsAll()
    {
        // Arrange
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("zh-CN", "Auth.Login");

        // Act
        var missing = await _tracker.GetMissingKeysAsync();

        // Assert
        Assert.Equal(2, missing.Count);
    }

    [Fact]
    public async Task GetSummaryAsync_MultipleCultures_ReturnCorrectBreakdown()
    {
        // Arrange
        _tracker.TrackMissing("en", "Key1");
        _tracker.TrackMissing("en", "Key2");
        _tracker.TrackMissing("en", "Key1"); // second access
        _tracker.TrackMissing("zh-CN", "Key1");

        // Act
        var summary = await _tracker.GetSummaryAsync();

        // Assert
        Assert.Equal(3, summary.TotalMissingKeys); // en:Key1, en:Key2, zh-CN:Key1
        Assert.Equal(4, summary.TotalAccessCount); // 2 + 1 + 1
        Assert.Equal(2, summary.AffectedCultureCount);
        Assert.Contains(summary.CultureBreakdown, c => c.Culture == "en" && c.MissingKeyCount == 2);
        Assert.Contains(summary.CultureBreakdown, c => c.Culture == "zh-CN" && c.MissingKeyCount == 1);
    }

    [Fact]
    public async Task ExportMissingAsync_ReturnsGroupedByCulture()
    {
        // Arrange
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("en", "Auth.Logout");
        _tracker.TrackMissing("zh-CN", "Auth.Login");

        // Act
        var export = await _tracker.ExportMissingAsync();

        // Assert
        Assert.Equal(2, export.Count);
        Assert.True(export.ContainsKey("en"));
        Assert.True(export.ContainsKey("zh-CN"));
        Assert.Equal(2, export["en"].Count);
        Assert.Single(export["zh-CN"]);
        // Values should be empty strings (stubs for translation)
        Assert.All(export["en"].Values, v => Assert.Equal(string.Empty, v));
    }

    [Fact]
    public async Task ExportMissingAsync_FilterByCulture_ReturnsOnlyMatchingCulture()
    {
        // Arrange
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("zh-CN", "Auth.Login");

        // Act
        var export = await _tracker.ExportMissingAsync("en");

        // Assert
        Assert.Single(export);
        Assert.True(export.ContainsKey("en"));
        Assert.False(export.ContainsKey("zh-CN"));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        // Arrange
        _tracker.TrackMissing("en", "Auth.Login");
        _tracker.TrackMissing("zh-CN", "Auth.Logout");

        // Act
        await _tracker.ClearAsync();

        // Assert
        var missing = await _tracker.GetMissingKeysAsync();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task TrackMissing_UpdatesLastAccessTime()
    {
        // Act
        _tracker.TrackMissing("en", "Auth.Login");
        var firstAccess = (await _tracker.GetMissingKeysAsync())[0].LastAccessTime;

        // Wait a small amount and track again
        await Task.Delay(20);
        _tracker.TrackMissing("en", "Auth.Login");
        var secondAccess = (await _tracker.GetMissingKeysAsync())[0].LastAccessTime;

        // Assert
        Assert.True(secondAccess >= firstAccess);
        Assert.Equal(2, (await _tracker.GetMissingKeysAsync())[0].AccessCount);
    }

    [Fact]
    public async Task TrackMissing_InDevelopment_LogsWarning()
    {
        // Act
        _tracker.TrackMissing("en", "Auth.Login");

        // Assert — verify logger was called
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Missing translation")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Second track of same key should NOT log again
        _tracker.TrackMissing("en", "Auth.Login");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Missing translation")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once); // still only once
    }

    [Fact]
    public async Task TrackMissing_InProduction_DoesNotLog_ButStillTracks()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<MissingTranslationTracker>>();
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");
        var prodTracker = new MissingTranslationTracker(loggerMock.Object, envMock.Object);

        // Act
        prodTracker.TrackMissing("en", "Auth.Login");

        // Assert — no log warning in production
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        // But still tracked
        var missing = await prodTracker.GetMissingKeysAsync();
        Assert.Single(missing);
    }

    [Fact]
    public async Task GetSummaryAsync_Empty_ReturnsZeros()
    {
        // Act
        var summary = await _tracker.GetSummaryAsync();

        // Assert
        Assert.Equal(0, summary.TotalMissingKeys);
        Assert.Equal(0, summary.TotalAccessCount);
        Assert.Equal(0, summary.AffectedCultureCount);
        Assert.Empty(summary.CultureBreakdown);
    }
}
