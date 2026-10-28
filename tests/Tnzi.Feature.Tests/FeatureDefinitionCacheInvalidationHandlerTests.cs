using Moq;
using Tnzi.Feature.Events;
using Tnzi.Feature.Services;

namespace Tnzi.Feature.Tests;

/// <summary>
/// 定义写入之后再失效一次定义快照：事件经事务感知发布在提交之后才派发，服务里那一次就地失效发生在提交之前，
/// 环境工作单元下并发的刷新会把旧定义再缓存一整个刷新间隔。
/// </summary>
public class FeatureDefinitionCacheInvalidationHandlerTests
{
    private readonly Mock<IFeatureManager> _manager = new();

    private FeatureDefinitionCacheInvalidationHandler Handler => new(_manager.Object);

    [Fact]
    public async Task DefinitionCreated_InvalidatesTheSnapshot()
    {
        await Handler.HandleAsync(new FeatureDefinitionCreatedEvent { Name = "Feature.A" });

        _manager.Verify(m => m.InvalidateCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task DefinitionUpdated_InvalidatesTheSnapshot()
    {
        await Handler.HandleAsync(new FeatureDefinitionUpdatedEvent { Name = "Feature.A", IsEnabled = false });

        _manager.Verify(m => m.InvalidateCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task DefinitionDeleted_InvalidatesTheSnapshot()
    {
        await Handler.HandleAsync(new FeatureDefinitionDeletedEvent { Name = "Feature.A" });

        _manager.Verify(m => m.InvalidateCacheAsync(), Times.Once);
    }
}
