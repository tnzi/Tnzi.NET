
namespace Tnzi.Storage.Tests;

/// <summary>
/// CleanupOptions 配置测试
/// </summary>
public class CleanupOptionsTests
{
    [Fact]
    public void CleanupOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new CleanupOptions();

        // Assert
        Assert.False(options.Enabled); // 默认值是 false
        Assert.Equal(60, options.IntervalMinutes);
        Assert.Equal(24, options.TemporaryFileRetentionHours);
        Assert.True(options.EnableOrphanFileCleanup);
        Assert.Equal(72, options.OrphanFileRetentionHours);
        Assert.False(options.EnableOrphanReferenceCleanup);
        Assert.Equal(100, options.MaxFilesPerRun);
        Assert.Null(options.CronExpression);
    }

    [Fact]
    public void CleanupOptions_CanBeConfigured()
    {
        // Arrange & Act
        var options = new CleanupOptions
        {
            Enabled = false,
            IntervalMinutes = 30,
            TemporaryFileRetentionHours = 12,
            EnableOrphanFileCleanup = false,
            OrphanFileRetentionHours = 48,
            EnableOrphanReferenceCleanup = true,
            MaxFilesPerRun = 50,
            CronExpression = "0 3 * * *"
        };

        // Assert
        Assert.False(options.Enabled);
        Assert.Equal(30, options.IntervalMinutes);
        Assert.Equal(12, options.TemporaryFileRetentionHours);
        Assert.False(options.EnableOrphanFileCleanup);
        Assert.Equal(48, options.OrphanFileRetentionHours);
        Assert.True(options.EnableOrphanReferenceCleanup);
        Assert.Equal(50, options.MaxFilesPerRun);
        Assert.Equal("0 3 * * *", options.CronExpression);
    }

    [Fact]
    public void StorageOptions_Cleanup_DefaultsToNewInstance()
    {
        // Arrange & Act
        var options = new StorageOptions();

        // Assert
        Assert.NotNull(options.Cleanup);
        Assert.False(options.Cleanup.Enabled); // 默认值是 false
    }

    [Fact]
    public void StorageOptionsValidator_ShouldPassWithDefaultOptions()
    {
        // Arrange
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void StorageOptionsValidator_ShouldFailWhenMaxFileSizeIsZero()
    {
        // Arrange
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { MaxFileSize = 0 };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("MaxFileSize", result.FailureMessage);
    }
}