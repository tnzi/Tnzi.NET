using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// ParentDocumentRetriever 单元测试 — 验证块扩展、合并、去重和 Token 截断
/// </summary>
public class ParentDocumentRetrieverTests
{
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();

    private readonly Guid _docId = Guid.NewGuid();
    private readonly Guid _kbId = Guid.NewGuid();

    private ParentDocumentRetriever CreateService()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        return new ParentDocumentRetriever(
            serviceProviderMock.Object,
            _chunkRepoMock.Object,
            _docRepoMock.Object);
    }

    private void SetupChunks(params (int Index, string Content)[] chunks)
    {
        var entities = chunks.Select(c => new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = _docId,
            KnowledgeBaseId = _kbId,
            ChunkIndex = c.Index,
            Content = c.Content
        }).ToList();

        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(entities.BuildMock());
    }

    private void SetupDocumentName(string fileName)
    {
        var docs = new List<KnowledgeDocument>
        {
            new()
            {
                Id = _docId,
                KnowledgeBaseId = _kbId,
                FileName = fileName
            }
        };

        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(docs.BuildMock());
    }

    private RetrievalResult CreateMatch(int chunkIndex, double score, string content = "match")
    {
        return new RetrievalResult
        {
            Content = content,
            Score = score,
            DocumentId = _docId,
            KnowledgeBaseId = _kbId,
            Metadata = new Dictionary<string, object>
            {
                ["chunkIndex"] = chunkIndex
            }
        };
    }

    #region Basic Expansion

    [Fact]
    public async Task RetrieveAsync_EmptyInput_ReturnsEmpty()
    {
        var sut = CreateService();
        var results = await sut.RetrieveAsync([]);
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetrieveAsync_SingleMatch_ExpandsWindow()
    {
        // Arrange: 10 块（0-9），匹配块 5，窗口大小 2 → 应加载 3-7
        SetupChunks(
            (0, "c0"), (1, "c1"), (2, "c2"), (3, "c3"), (4, "c4"),
            (5, "c5"), (6, "c6"), (7, "c7"), (8, "c8"), (9, "c9"));
        SetupDocumentName("test.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult> { CreateMatch(5, 0.9) };

        // Act
        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 2 });

        // Assert
        results.Count.ShouldBe(1);
        var result = results[0];
        result.DocumentId.ShouldBe(_docId);
        result.StartChunkIndex.ShouldBe(3);
        result.EndChunkIndex.ShouldBe(7);
        result.Score.ShouldBe(0.9);
        result.DocumentName.ShouldBe("test.pdf");
        result.MergedContent.ShouldContain("c3");
        result.MergedContent.ShouldContain("c5");
        result.MergedContent.ShouldContain("c7");
    }

    [Fact]
    public async Task RetrieveAsync_MatchAtStart_ClampsToBoundary()
    {
        // Arrange: 匹配块 1，窗口 2 → 应加载 0-3（不能低于 0）
        SetupChunks((0, "c0"), (1, "c1"), (2, "c2"), (3, "c3"));
        SetupDocumentName("doc.md");

        var sut = CreateService();
        var matches = new List<RetrievalResult> { CreateMatch(1, 0.8) };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 2 });

        results.Count.ShouldBe(1);
        results[0].StartChunkIndex.ShouldBe(0);
        results[0].MergedContent.ShouldContain("c0");
    }

    #endregion

    #region Merging Adjacent Matches

    [Fact]
    public async Task RetrieveAsync_AdjacentMatches_MergeIntoSingleBlock()
    {
        // Arrange: 匹配块 3 和 5，窗口 2 → 范围 1-7，重叠应合并为一个块
        SetupChunks(
            (1, "c1"), (2, "c2"), (3, "c3"), (4, "c4"),
            (5, "c5"), (6, "c6"), (7, "c7"));
        SetupDocumentName("merged.txt");

        var sut = CreateService();
        var matches = new List<RetrievalResult>
        {
            CreateMatch(3, 0.7),
            CreateMatch(5, 0.9)
        };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 2 });

        // 范围 [1,5] 和 [3,7] 重叠 → 合并为 [1,7]
        results.Count.ShouldBe(1);
        results[0].Score.ShouldBe(0.9); // 取最大分数
        results[0].MergedContent.ShouldContain("c1");
        results[0].MergedContent.ShouldContain("c7");
    }

    [Fact]
    public async Task RetrieveAsync_DisjointMatches_ReturnSeparateBlocks()
    {
        // Arrange: 匹配块 1 和 9，窗口 1 → [0,2] 和 [8,10]，不重叠
        SetupChunks(
            (0, "c0"), (1, "c1"), (2, "c2"),
            (8, "c8"), (9, "c9"), (10, "c10"));
        SetupDocumentName("disjoint.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult>
        {
            CreateMatch(1, 0.6),
            CreateMatch(9, 0.95)
        };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 1 });

        results.Count.ShouldBe(2);
        // 按分数降序排列
        results[0].Score.ShouldBe(0.95);
        results[1].Score.ShouldBe(0.6);
    }

    #endregion

    #region MaxTokens Truncation

    [Fact]
    public async Task RetrieveAsync_ExceedsMaxTokens_Truncates()
    {
        // Arrange: 每个块 5000 字符 ≈ 1250 token，MaxTokens=2000 → 最多包含 1-2 块
        var longContent = new string('x', 5000);
        SetupChunks(
            (3, longContent), (4, longContent), (5, longContent),
            (6, longContent), (7, longContent));
        SetupDocumentName("big.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult> { CreateMatch(5, 0.85) };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions
        {
            WindowSize = 2,
            MaxTokens = 2000
        });

        results.Count.ShouldBe(1);
        // 合并内容不应包含所有 5 个块
        var partCount = results[0].MergedContent.Split('\n').Length;
        partCount.ShouldBeLessThan(5);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task RetrieveAsync_NoDocumentId_ReturnsEmpty()
    {
        var sut = CreateService();
        var matches = new List<RetrievalResult>
        {
            new()
            {
                Content = "test",
                Score = 0.9,
                DocumentId = Guid.Empty,
                Metadata = new Dictionary<string, object> { ["chunkIndex"] = 0 }
            }
        };

        var results = await sut.RetrieveAsync(matches);
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetrieveAsync_NoChunkIndexInMetadata_ReturnsEmpty()
    {
        SetupChunks((0, "c0"));
        SetupDocumentName("test.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult>
        {
            new()
            {
                Content = "test",
                Score = 0.9,
                DocumentId = _docId,
                Metadata = new Dictionary<string, object>() // 无 chunkIndex
            }
        };

        var results = await sut.RetrieveAsync(matches);
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetrieveAsync_DefaultOptions_UsesDefaultWindowAndMaxTokens()
    {
        SetupChunks(
            (0, "c0"), (1, "c1"), (2, "c2"), (3, "c3"), (4, "c4"));
        SetupDocumentName("default.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult> { CreateMatch(2, 0.8) };

        // 默认 WindowSize=2，应加载 0-4
        var results = await sut.RetrieveAsync(matches);

        results.Count.ShouldBe(1);
        results[0].StartChunkIndex.ShouldBe(0);
        results[0].EndChunkIndex.ShouldBe(4);
    }

    [Fact]
    public async Task RetrieveAsync_ChunkIndexAsJsonElement_ParsesCorrectly()
    {
        SetupChunks((2, "c2"), (3, "c3"), (4, "c4"));
        SetupDocumentName("json.pdf");

        var sut = CreateService();

        // 模拟从 JSON 反序列化得到的 JsonElement
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>("{\"chunkIndex\": 3}")!;
        var matches = new List<RetrievalResult>
        {
            new()
            {
                Content = "match",
                Score = 0.9,
                DocumentId = _docId,
                KnowledgeBaseId = _kbId,
                Metadata = json
            }
        };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 1 });

        results.Count.ShouldBe(1);
        results[0].MergedContent.ShouldContain("c3");
    }

    [Fact]
    public async Task RetrieveAsync_MissingChunksInDb_SkipsMissing()
    {
        // 数据库中缺少块 4（可能已被删除）
        SetupChunks((3, "c3"), (5, "c5"), (6, "c6"), (7, "c7"));
        SetupDocumentName("sparse.pdf");

        var sut = CreateService();
        var matches = new List<RetrievalResult> { CreateMatch(5, 0.9) };

        var results = await sut.RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = 2 });

        results.Count.ShouldBe(1);
        // 块 4 缺失，合并内容中不应包含它
        results[0].MergedContent.ShouldNotContain("c4");
        results[0].MergedContent.ShouldContain("c3");
        results[0].MergedContent.ShouldContain("c5");
    }

    #endregion
}
