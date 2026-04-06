using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// GraphSearchService 单元测试 — 验证图谱搜索的名称/类型匹配、关系加载和上下文片段生成
/// </summary>
public class GraphSearchServiceTests
{
    private readonly Mock<IRepository<KnowledgeGraphNode, Guid>> _nodeRepoMock = new();
    private readonly Mock<IRepository<KnowledgeGraphEdge, Guid>> _edgeRepoMock = new();

    private readonly Guid _kbId = Guid.NewGuid();

    private GraphSearchService CreateService()
    {
        return new GraphSearchService(
            _nodeRepoMock.Object,
            _edgeRepoMock.Object,
            NullLogger<GraphSearchService>.Instance);
    }

    #region Helper Methods

    private KnowledgeGraphNode CreateNode(string name, string entityType, string? description = null, Guid? id = null)
    {
        return new KnowledgeGraphNode
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            EntityType = entityType,
            Description = description,
            KnowledgeBaseId = _kbId
        };
    }

    private KnowledgeGraphEdge CreateEdge(Guid sourceId, Guid targetId, string relationType, double? weight = null)
    {
        return new KnowledgeGraphEdge
        {
            Id = Guid.NewGuid(),
            SourceNodeId = sourceId,
            TargetNodeId = targetId,
            RelationType = relationType,
            Weight = weight,
            KnowledgeBaseId = _kbId
        };
    }

    private void SetupNodeQuery(List<KnowledgeGraphNode> nodes)
    {
        _nodeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(nodes.BuildMock());
    }

    private void SetupEdgeQuery(List<KnowledgeGraphEdge> edges)
    {
        _edgeRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(edges.BuildMock());
    }

    #endregion

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var service = CreateService();

        var results = await service.SearchAsync("", _kbId);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var service = CreateService();

        var results = await service.SearchAsync("   ", _kbId);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NoMatchingNodes_ReturnsEmpty()
    {
        var nodes = new List<KnowledgeGraphNode>
        {
            CreateNode("Alice", "Person"),
            CreateNode("Bob", "Person")
        };
        SetupNodeQuery(nodes);

        var service = CreateService();
        var results = await service.SearchAsync("NonexistentEntity", _kbId);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ExactNameMatch_ReturnsScore1()
    {
        var node = CreateNode("Alice", "Person", "A software engineer");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        results.Count.ShouldBe(1);
        results[0].NodeName.ShouldBe("Alice");
        results[0].NodeType.ShouldBe("Person");
        results[0].Score.ShouldBe(1.0);
    }

    [Fact]
    public async Task SearchAsync_ExactNameMatch_CaseInsensitive()
    {
        var node = CreateNode("Alice", "Person");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("alice", _kbId);

        results.Count.ShouldBe(1);
        results[0].Score.ShouldBe(1.0);
    }

    [Fact]
    public async Task SearchAsync_PartialNameMatch_ReturnsScore07()
    {
        var node = CreateNode("Alice Johnson", "Person");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        results.Count.ShouldBe(1);
        results[0].Score.ShouldBe(0.7);
    }

    [Fact]
    public async Task SearchAsync_QueryContainsNodeName_ReturnsScore07()
    {
        var node = CreateNode("AI", "Concept");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("What is AI technology", _kbId);

        results.Count.ShouldBe(1);
        results[0].Score.ShouldBe(0.7);
    }

    [Fact]
    public async Task SearchAsync_TypeMatch_ReturnsScore03()
    {
        var node = CreateNode("Alice", "Person");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Person", _kbId);

        results.Count.ShouldBe(1);
        results[0].Score.ShouldBe(0.3);
    }

    [Fact]
    public async Task SearchAsync_MultipleMatches_OrderedByScore()
    {
        var exactNode = CreateNode("Machine Learning", "Concept");
        var partialNode = CreateNode("Machine Learning Engineering", "Concept");
        var typeNode = CreateNode("Python", "Concept");

        SetupNodeQuery([exactNode, partialNode, typeNode]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Machine Learning", _kbId);

        results.Count.ShouldBe(2); // exactNode + partialNode match; typeNode "Concept" doesn't match "machine learning"
        results[0].Score.ShouldBe(1.0);  // exact
        results[1].Score.ShouldBe(0.7);  // partial
    }

    [Fact]
    public async Task SearchAsync_MaxResultsLimitsOutput()
    {
        var nodes = Enumerable.Range(1, 10)
            .Select(i => CreateNode($"Node{i}", "Concept"))
            .ToList();
        SetupNodeQuery(nodes);
        SetupEdgeQuery([]);

        var service = CreateService();
        var options = new GraphSearchOptions(MaxResults: 3);
        var results = await service.SearchAsync("Node", _kbId, options);

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SearchAsync_WithRelationships_LoadsRelatedNodes()
    {
        var aliceId = Guid.NewGuid();
        var acmeId = Guid.NewGuid();
        var alice = CreateNode("Alice", "Person", "An engineer", aliceId);
        var acme = CreateNode("Acme Corp", "Organization", "A tech company", acmeId);
        var edge = CreateEdge(aliceId, acmeId, "works_for", 0.95);

        SetupNodeQuery([alice, acme]);
        SetupEdgeQuery([edge]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        results.Count.ShouldBeGreaterThanOrEqualTo(1);
        var aliceResult = results.First(r => r.NodeName == "Alice");
        aliceResult.RelatedNodes.Count.ShouldBe(1);
        aliceResult.RelatedNodes[0].NodeName.ShouldBe("Acme Corp");
        aliceResult.RelatedNodes[0].RelationType.ShouldBe("works_for");
        aliceResult.RelatedNodes[0].Direction.ShouldBe(RelationDirection.Outgoing);
    }

    [Fact]
    public async Task SearchAsync_WithRelationships_IncomingDirection()
    {
        var aliceId = Guid.NewGuid();
        var acmeId = Guid.NewGuid();
        var alice = CreateNode("Alice", "Person", id: aliceId);
        var acme = CreateNode("Acme Corp", "Organization", id: acmeId);
        // Acme → employs → Alice (Alice is target)
        var edge = CreateEdge(acmeId, aliceId, "employs");

        SetupNodeQuery([alice, acme]);
        SetupEdgeQuery([edge]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        var aliceResult = results.First(r => r.NodeName == "Alice");
        aliceResult.RelatedNodes.Count.ShouldBe(1);
        aliceResult.RelatedNodes[0].NodeName.ShouldBe("Acme Corp");
        aliceResult.RelatedNodes[0].Direction.ShouldBe(RelationDirection.Incoming);
    }

    [Fact]
    public async Task SearchAsync_IncludeRelationshipsFalse_NoRelatedNodes()
    {
        var aliceId = Guid.NewGuid();
        var acmeId = Guid.NewGuid();
        var alice = CreateNode("Alice", "Person", id: aliceId);
        var acme = CreateNode("Acme Corp", "Organization", id: acmeId);
        var edge = CreateEdge(aliceId, acmeId, "works_for");

        SetupNodeQuery([alice, acme]);
        SetupEdgeQuery([edge]);

        var service = CreateService();
        var options = new GraphSearchOptions(IncludeRelationships: false);
        var results = await service.SearchAsync("Alice", _kbId, options);

        var aliceResult = results.First(r => r.NodeName == "Alice");
        aliceResult.RelatedNodes.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ContextSnippet_ContainsEntityInfo()
    {
        var node = CreateNode("Alice", "Person", "A software engineer");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        results[0].ContextSnippet.ShouldContain("Entity: Alice (Person)");
        results[0].ContextSnippet.ShouldContain("A software engineer");
    }

    [Fact]
    public async Task SearchAsync_ContextSnippet_ContainsRelationships()
    {
        var aliceId = Guid.NewGuid();
        var acmeId = Guid.NewGuid();
        var alice = CreateNode("Alice", "Person", id: aliceId);
        var acme = CreateNode("Acme Corp", "Organization", id: acmeId);
        var edge = CreateEdge(aliceId, acmeId, "works_for");

        SetupNodeQuery([alice, acme]);
        SetupEdgeQuery([edge]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        var aliceResult = results.First(r => r.NodeName == "Alice");
        aliceResult.ContextSnippet.ShouldContain("Related:");
        aliceResult.ContextSnippet.ShouldContain("works_for → Acme Corp");
    }

    [Fact]
    public async Task SearchAsync_NoDescription_SnippetOmitsDescription()
    {
        var node = CreateNode("Alice", "Person");
        SetupNodeQuery([node]);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Alice", _kbId);

        results[0].ContextSnippet.ShouldBe("Entity: Alice (Person).");
    }

    [Fact]
    public async Task SearchAsync_DefaultOptions_UsedWhenNull()
    {
        var nodes = Enumerable.Range(1, 10)
            .Select(i => CreateNode($"Node{i}", "Concept"))
            .ToList();
        SetupNodeQuery(nodes);
        SetupEdgeQuery([]);

        var service = CreateService();
        var results = await service.SearchAsync("Node", _kbId, options: null);

        // Default MaxResults is 5
        results.Count.ShouldBe(5);
    }
}
