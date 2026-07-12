using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// KnowledgeBaseService.DeleteAsync 单元测试 — 验证软删 KB 时图谱节点/边被显式删除
/// （KB 软删不触发 FK 级联，图谱数据须与 chunk/doc 同等对待，否则永久残留悬挂数据）。
/// </summary>
public class KnowledgeBaseServiceDeleteTests
{
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock = new();
    private readonly Mock<IRepository<KnowledgeGraphNode, Guid>> _graphNodeRepoMock = new();
    private readonly Mock<IRepository<KnowledgeGraphEdge, Guid>> _graphEdgeRepoMock = new();
    private readonly Mock<IDocumentIngestionService> _ingestionServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IReranker> _rerankerMock = new();

    private KnowledgeBaseService CreateService()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var options = new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions());

        return new KnowledgeBaseService(
            _kbRepoMock.Object,
            _docRepoMock.Object,
            _chunkRepoMock.Object,
            _graphNodeRepoMock.Object,
            _graphEdgeRepoMock.Object,
            _ingestionServiceMock.Object,
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _rerankerMock.Object,
            options,
            serviceProviderMock.Object,
            backgroundJobManager: null);
    }

    [Fact]
    public async Task DeleteAsync_KnowledgeBaseNotFound_Returns404()
    {
        var kbId = Guid.NewGuid();
        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeBase?)null);

        var service = CreateService();
        var result = await service.DeleteAsync(kbId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_DeletesGraphNodesAndEdges_ScopedToKnowledgeBase()
    {
        var kbId = Guid.NewGuid();
        var otherKbId = Guid.NewGuid();
        var kb = new KnowledgeBase { Id = kbId, Name = "KB" };

        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeDocument>().BuildMock());

        var nodes = new List<KnowledgeGraphNode>
        {
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, EntityType = "Person", Name = "Alice" },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, EntityType = "Org", Name = "Acme" },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = otherKbId, EntityType = "Person", Name = "Bob" }
        };
        var edges = new List<KnowledgeGraphEdge>
        {
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, SourceNodeId = nodes[0].Id, TargetNodeId = nodes[1].Id, RelationType = "works_for" },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = otherKbId, SourceNodeId = nodes[2].Id, TargetNodeId = nodes[2].Id, RelationType = "self" }
        };

        _graphNodeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(nodes.BuildMock());
        _graphEdgeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(edges.BuildMock());

        List<KnowledgeGraphNode>? deletedNodes = null;
        List<KnowledgeGraphEdge>? deletedEdges = null;
        var deletionOrder = new List<string>();

        _graphNodeRepoMock
            .Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<KnowledgeGraphNode>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeGraphNode>, CancellationToken>((batch, _) =>
            {
                deletedNodes = batch.ToList();
                deletionOrder.Add("nodes");
            })
            .Returns(Task.CompletedTask);
        _graphEdgeRepoMock
            .Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<KnowledgeGraphEdge>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeGraphEdge>, CancellationToken>((batch, _) =>
            {
                deletedEdges = batch.ToList();
                deletionOrder.Add("edges");
            })
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.DeleteAsync(kbId);

        result.Succeeded.ShouldBeTrue();

        // 只删本 KB 的图谱数据
        deletedEdges.ShouldNotBeNull();
        deletedEdges!.Count.ShouldBe(1);
        deletedEdges.All(e => e.KnowledgeBaseId == kbId).ShouldBeTrue();

        deletedNodes.ShouldNotBeNull();
        deletedNodes!.Count.ShouldBe(2);
        deletedNodes.All(n => n.KnowledgeBaseId == kbId).ShouldBeTrue();

        // 先删边（边对节点有 FK）再删节点
        deletionOrder.ShouldBe(["edges", "nodes"]);

        // 既有级联仍然执行
        _vectorStoreMock.Verify(v => v.DeleteByKnowledgeBaseAsync(kbId, It.IsAny<CancellationToken>()), Times.Once);
        _kbRepoMock.Verify(r => r.DeleteAsync(kb, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NoGraphData_SkipsGraphDeletion()
    {
        var kbId = Guid.NewGuid();
        var kb = new KnowledgeBase { Id = kbId, Name = "KB" };

        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeDocument>().BuildMock());
        _graphNodeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeGraphNode>().BuildMock());
        _graphEdgeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeGraphEdge>().BuildMock());

        var service = CreateService();
        var result = await service.DeleteAsync(kbId);

        result.Succeeded.ShouldBeTrue();
        _graphNodeRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<KnowledgeGraphNode>>(), It.IsAny<CancellationToken>()), Times.Never);
        _graphEdgeRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<KnowledgeGraphEdge>>(), It.IsAny<CancellationToken>()), Times.Never);
        _kbRepoMock.Verify(r => r.DeleteAsync(kb, It.IsAny<CancellationToken>()), Times.Once);
    }
}
