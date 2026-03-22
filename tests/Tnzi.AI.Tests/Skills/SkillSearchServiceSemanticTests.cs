using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillSearchService 语义降级（Tier 2）单元测试
/// </summary>
public class SkillSearchServiceSemanticTests
{
    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// 构建不含任何关键词命中的候选（名称、描述、标签均不含 query token）
    /// </summary>
    private static List<SkillDefinition> BuildSemanticOnlyCandidates() =>
    [
        new SkillDefinition
        {
            Slug = "alpha",
            Name = "Alpha Ability",
            Description = "Handles complex reasoning tasks.",
            Tags = ["reasoning"],
            WhenToUse = "When deep analysis is needed."
        },
        new SkillDefinition
        {
            Slug = "beta",
            Name = "Beta Ability",
            Description = "Manages workflow orchestration.",
            Tags = ["workflow"],
            WhenToUse = "When coordinating multiple steps."
        }
    ];

    /// <summary>
    /// 构建候选：前 2 个能被关键词 "code" 命中，后 2 个不能
    /// </summary>
    private static List<SkillDefinition> BuildMixedCandidates() =>
    [
        new SkillDefinition
        {
            Slug = "code-review",
            Name = "Code Review",
            Description = "Analyzes code quality.",
            Tags = ["code"],
            WhenToUse = "Use for code review."
        },
        new SkillDefinition
        {
            Slug = "code-gen",
            Name = "Code Generator",
            Description = "Generates code from specs.",
            Tags = ["code"],
            WhenToUse = "Use to generate code."
        },
        new SkillDefinition
        {
            Slug = "alpha",
            Name = "Alpha Ability",
            Description = "Handles complex reasoning tasks.",
            Tags = ["reasoning"],
            WhenToUse = "When deep analysis is needed."
        },
        new SkillDefinition
        {
            Slug = "beta",
            Name = "Beta Ability",
            Description = "Manages workflow orchestration.",
            Tags = ["workflow"],
            WhenToUse = "When coordinating multiple steps."
        }
    ];

    /// <summary>
    /// 创建返回高相似度嵌入的 mock IEmbeddingService
    /// </summary>
    private static Mock<IEmbeddingService> CreateHighSimilarityEmbeddingService()
    {
        var mock = new Mock<IEmbeddingService>();

        // 查询向量: [1, 0, 0]
        mock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new float[] { 1f, 0f, 0f }));

        // 候选向量: 全部返回与查询高度相似的向量
        mock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
            {
                // 所有候选都返回接近查询的向量 → 高相似度
                var embeddings = texts.Select(_ => new float[] { 0.9f, 0.1f, 0.1f }).ToList();
                return Result<List<float[]>>.Success(embeddings);
            });

        return mock;
    }

    /// <summary>
    /// 创建返回低相似度嵌入的 mock IEmbeddingService
    /// </summary>
    private static Mock<IEmbeddingService> CreateLowSimilarityEmbeddingService()
    {
        var mock = new Mock<IEmbeddingService>();

        // 查询向量
        mock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new float[] { 1f, 0f, 0f }));

        // 候选向量: 全部返回与查询不相似的向量（正交）
        mock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
            {
                var embeddings = texts.Select(_ => new float[] { 0f, 1f, 0f }).ToList();
                return Result<List<float[]>>.Success(embeddings);
            });

        return mock;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_KeywordSufficient_NoSemanticFallback()
    {
        // Arrange: 关键词 "code" 能命中 2 个候选，maxResults=2 → 不触发语义降级
        var embeddingMock = CreateHighSimilarityEmbeddingService();
        var service = new SkillSearchService(
            logger: NullLogger<SkillSearchService>.Instance,
            embeddingService: embeddingMock.Object);

        var candidates = BuildMixedCandidates();

        // Act
        var results = await service.SearchAsync(candidates, "code", maxResults: 2);

        // Assert: 返回 2 个关键词结果
        results.Count.ShouldBe(2);
        results.All(r => r.Slug is "code-review" or "code-gen").ShouldBeTrue();

        // 嵌入服务不应被调用
        embeddingMock.Verify(
            e => e.GenerateEmbeddingAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_NoKeywordMatch_SemanticReturnsResults()
    {
        // Arrange: 查询 "xyz" 不命中任何关键词，但语义搜索返回高相似度
        var embeddingMock = CreateHighSimilarityEmbeddingService();
        var service = new SkillSearchService(
            logger: NullLogger<SkillSearchService>.Instance,
            embeddingService: embeddingMock.Object);

        var candidates = BuildSemanticOnlyCandidates();

        // Act
        var results = await service.SearchAsync(candidates, "xyz", maxResults: 5);

        // Assert: 语义搜索应返回结果
        results.ShouldNotBeEmpty();

        // 嵌入服务应被调用
        embeddingMock.Verify(
            e => e.GenerateEmbeddingAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
        embeddingMock.Verify(
            e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingServiceNull_GracefulDegradation()
    {
        // Arrange: 无嵌入服务，查询不命中任何关键词
        var service = new SkillSearchService(
            logger: NullLogger<SkillSearchService>.Instance,
            embeddingService: null);

        var candidates = BuildSemanticOnlyCandidates();

        // Act
        var results = await service.SearchAsync(candidates, "xyz", maxResults: 5);

        // Assert: 无嵌入服务时应返回空结果（不抛异常）
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_SemanticBelowThreshold_FilteredOut()
    {
        // Arrange: 语义相似度低于阈值 0.5（正交向量 → 相似度≈0）
        var embeddingMock = CreateLowSimilarityEmbeddingService();
        var service = new SkillSearchService(
            logger: NullLogger<SkillSearchService>.Instance,
            embeddingService: embeddingMock.Object);

        var candidates = BuildSemanticOnlyCandidates();

        // Act
        var results = await service.SearchAsync(candidates, "xyz", maxResults: 5);

        // Assert: 低相似度候选应被过滤，返回空
        results.ShouldBeEmpty();

        // 嵌入服务应被调用（尝试了语义搜索，只是结果被过滤）
        embeddingMock.Verify(
            e => e.GenerateEmbeddingAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
