namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// LlmGraphExtractor 单元测试 — 验证知识图谱实体关系提取
/// </summary>
public class GraphExtractorTests
{
    private readonly Mock<IAiUtility> _aiUtilityMock = new();
    private readonly Mock<IRepository<KnowledgeGraphNode, Guid>> _nodeRepoMock = new();
    private readonly Mock<IRepository<KnowledgeGraphEdge, Guid>> _edgeRepoMock = new();

    private LlmGraphExtractor CreateExtractor()
    {
        return new LlmGraphExtractor(
            _aiUtilityMock.Object,
            _nodeRepoMock.Object,
            _edgeRepoMock.Object,
            NullLogger<LlmGraphExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_ValidText_ReturnsNodesAndEdges()
    {
        // Arrange
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "Alice", "type": "Person", "description": "A software engineer" },
                { "name": "Acme Corp", "type": "Organization", "description": "A tech company" }
              ],
              "relationships": [
                { "source": "Alice", "target": "Acme Corp", "relation": "works_for", "description": "Alice is employed by Acme Corp", "weight": 0.95 }
              ]
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();

        // Act
        var result = await extractor.ExtractAsync("Alice works at Acme Corp as a software engineer.", kbId);

        // Assert
        result.Nodes.Count.ShouldBe(2);
        result.Edges.Count.ShouldBe(1);

        var alice = result.Nodes.First(n => n.Name == "Alice");
        alice.EntityType.ShouldBe("Person");
        alice.Description.ShouldBe("A software engineer");
        alice.KnowledgeBaseId.ShouldBe(kbId);

        var acme = result.Nodes.First(n => n.Name == "Acme Corp");
        acme.EntityType.ShouldBe("Organization");

        var edge = result.Edges[0];
        edge.RelationType.ShouldBe("works_for");
        edge.Weight.ShouldBe(0.95);
        edge.KnowledgeBaseId.ShouldBe(kbId);

        // 验证持久化调用
        _nodeRepoMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<KnowledgeGraphNode>>(n => n.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        _edgeRepoMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<KnowledgeGraphEdge>>(e => e.Count() == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_EmptyText_ReturnsEmptyResult()
    {
        var extractor = CreateExtractor();

        var result = await extractor.ExtractAsync("", Guid.NewGuid());

        result.ShouldBe(GraphExtractionResult.Empty);
        _aiUtilityMock.Verify(u => u.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_WhitespaceText_ReturnsEmptyResult()
    {
        var extractor = CreateExtractor();

        var result = await extractor.ExtractAsync("   \n\t  ", Guid.NewGuid());

        result.ShouldBe(GraphExtractionResult.Empty);
    }

    [Fact]
    public async Task ExtractAsync_LlmReturnsNull_ReturnsEmptyResult()
    {
        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", Guid.NewGuid());

        result.ShouldBe(GraphExtractionResult.Empty);
    }

    [Fact]
    public async Task ExtractAsync_MalformedJson_ReturnsEmptyResult()
    {
        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("this is not valid json {{{");

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", Guid.NewGuid());

        result.ShouldBe(GraphExtractionResult.Empty);
        _nodeRepoMock.Verify(r => r.InsertManyAsync(
            It.IsAny<IEnumerable<KnowledgeGraphNode>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_NoEntities_ReturnsEmptyResult()
    {
        var llmResponse = """{"entities": [], "relationships": []}""";

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", Guid.NewGuid());

        result.ShouldBe(GraphExtractionResult.Empty);
    }

    [Fact]
    public async Task ExtractAsync_EdgeReferencesNonexistentNode_SkipsInvalidEdge()
    {
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "Alice", "type": "Person", "description": "An engineer" }
              ],
              "relationships": [
                { "source": "Alice", "target": "NonexistentEntity", "relation": "knows", "weight": 0.5 }
              ]
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("Alice is an engineer.", kbId);

        result.Nodes.Count.ShouldBe(1);
        result.Edges.Count.ShouldBe(0);

        // 边引用了不存在的节点，不应调用 InsertManyAsync
        _edgeRepoMock.Verify(r => r.InsertManyAsync(
            It.IsAny<IEnumerable<KnowledgeGraphEdge>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_MultipleEntitiesAndRelationships_AllPersisted()
    {
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "Bob", "type": "Person" },
                { "name": "MIT", "type": "Organization" },
                { "name": "AI", "type": "Concept" }
              ],
              "relationships": [
                { "source": "Bob", "target": "MIT", "relation": "studies_at", "weight": 0.9 },
                { "source": "Bob", "target": "AI", "relation": "researches", "weight": 0.85 }
              ]
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("Bob studies AI at MIT.", kbId);

        result.Nodes.Count.ShouldBe(3);
        result.Edges.Count.ShouldBe(2);

        _nodeRepoMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<KnowledgeGraphNode>>(n => n.Count() == 3),
            It.IsAny<CancellationToken>()), Times.Once);
        _edgeRepoMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<KnowledgeGraphEdge>>(e => e.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_EntitiesWithMissingName_SkipsInvalid()
    {
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "", "type": "Person" },
                { "name": "Valid", "type": "Concept" },
                { "name": null, "type": "Location" }
              ],
              "relationships": []
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", kbId);

        result.Nodes.Count.ShouldBe(1);
        result.Nodes[0].Name.ShouldBe("Valid");
    }

    [Fact]
    public async Task ExtractAsync_RelationshipsWithMissingFields_SkipsInvalid()
    {
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "A", "type": "Concept" },
                { "name": "B", "type": "Concept" }
              ],
              "relationships": [
                { "source": "A", "target": "B", "relation": "related_to" },
                { "source": "", "target": "B", "relation": "invalid" },
                { "source": "A", "target": "", "relation": "invalid" },
                { "source": "A", "target": "B", "relation": "" }
              ]
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", kbId);

        result.Edges.Count.ShouldBe(1);
        result.Edges[0].RelationType.ShouldBe("related_to");
    }

    [Fact]
    public async Task ExtractAsync_OptionalWeightMissing_ReturnsNull()
    {
        var kbId = Guid.NewGuid();
        var llmResponse = """
            {
              "entities": [
                { "name": "X", "type": "Concept" }
              ],
              "relationships": [
                { "source": "X", "target": "X", "relation": "self_ref" }
              ]
            }
            """;

        _aiUtilityMock.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("some text", kbId);

        result.Edges.Count.ShouldBe(1);
        result.Edges[0].Weight.ShouldBeNull();
    }
}
