
namespace Tnzi.EFCore.Tests.Data;

/// <summary>
/// DataFilterManager 多租户过滤器审计日志测试
/// </summary>
public class DataFilterManagerTenantTests
{
    private Mock<ILogger<DataFilterManager>> CreateLoggerMock()
    {
        return new Mock<ILogger<DataFilterManager>>();
    }

    [Fact]
    public void Disable_MultiTenantFilter_LogsWarning()
    {
        // Arrange
        var loggerMock = CreateLoggerMock();
        var manager = new DataFilterManager(loggerMock.Object);

        // Act
        using var scope = manager.Disable<IMultiTenantFilter>();

        // Assert - 禁用多租户过滤器时应记录 Warning 日志
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state != null && state.ToString()!.Contains("Multi-tenant filter disabled")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// 多租户关着时没有租户隔离可绕过：不记那条「将绕过租户隔离」的警告（单租户部署每次启动、
    /// 工作流看门狗每一轮都会触发它）。开着时照记，见上一条。
    /// </summary>
    [Fact]
    public void Disable_MultiTenantFilter_WithMultiTenancyOff_DoesNotWarn()
    {
        var loggerMock = CreateLoggerMock();
        var options = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false });
        var manager = new DataFilterManager(loggerMock.Object, options);

        using var scope = manager.Disable<IMultiTenantFilter>();

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        Assert.False(manager.IsEnabled<IMultiTenantFilter>());
    }

    [Fact]
    public void Disable_MultiTenantFilter_WithMultiTenancyOn_StillWarns()
    {
        var loggerMock = CreateLoggerMock();
        var options = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true });
        var manager = new DataFilterManager(loggerMock.Object, options);

        using var scope = manager.Disable<IMultiTenantFilter>();

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state != null && state.ToString()!.Contains("Multi-tenant filter disabled")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Disable_NonTenantFilter_NoWarning()
    {
        // Arrange
        var loggerMock = CreateLoggerMock();
        var manager = new DataFilterManager(loggerMock.Object);

        // Act - 禁用软删除过滤器，不应产生租户相关警告
        using var scope = manager.Disable<ISoftDeleteFilter>();

        // Assert - 不应有任何 Warning 日志
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void Disable_ReturnsDisposable_ThatReEnablesFilter()
    {
        // Arrange
        var manager = new DataFilterManager();

        // 初始状态：多租户过滤器默认开启
        Assert.True(manager.IsEnabled<IMultiTenantFilter>());

        // Act - 禁用过滤器
        var scope = manager.Disable<IMultiTenantFilter>();
        Assert.False(manager.IsEnabled<IMultiTenantFilter>());

        // Dispose 后过滤器应自动恢复
        scope.Dispose();

        // Assert - Dispose 后过滤器重新启用
        Assert.True(manager.IsEnabled<IMultiTenantFilter>());
    }

    [Fact]
    public void Disable_MultiTenantFilter_WithoutLogger_DoesNotThrow()
    {
        // Arrange - logger 为 null 时不应抛异常
        var manager = new DataFilterManager(logger: null);

        // Act & Assert
        var ex = Record.Exception(() =>
        {
            using var scope = manager.Disable<IMultiTenantFilter>();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Disable_MultiTenantFilter_Nested_ReEnablesCorrectly()
    {
        // Arrange - 验证嵌套禁用的栈式行为
        var manager = new DataFilterManager();

        Assert.True(manager.IsEnabled<IMultiTenantFilter>());

        // Act - 嵌套禁用
        var outer = manager.Disable<IMultiTenantFilter>();
        Assert.False(manager.IsEnabled<IMultiTenantFilter>());

        var inner = manager.Disable<IMultiTenantFilter>();
        Assert.False(manager.IsEnabled<IMultiTenantFilter>());

        // 内层 Dispose
        inner.Dispose();
        Assert.False(manager.IsEnabled<IMultiTenantFilter>());

        // 外层 Dispose
        outer.Dispose();
        Assert.True(manager.IsEnabled<IMultiTenantFilter>());
    }
}
