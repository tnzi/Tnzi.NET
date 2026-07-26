using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

/// <summary>
/// 高级 RAG 模式单元测试 - 查询改写 + 相关性评分
/// </summary>
public class AdvancedRagTests
{
    #region NoOpQueryRewriter Tests

    [Fact]
    public async Task NoOpQueryRewriter_ReturnsOriginalQuery()
    {
        var rewriter = new NoOpQueryRewriter();

        var result = await rewriter.RewriteAsync("what is machine learning?");

        result.ShouldBe("what is machine learning?");
    }

    [Fact]
    public async Task NoOpQueryRewriter_EmptyQuery_ReturnsEmpty()
    {
        var rewriter = new NoOpQueryRewriter();

        var result = await rewriter.RewriteAsync(string.Empty);

        result.ShouldBe(string.Empty);
    }

    #endregion

    #region NoOpRelevanceGrader Tests

    [Fact]
    public async Task NoOpRelevanceGrader_MarksAllRelevant()
    {
        var grader = new NoOpRelevanceGrader();
        var results = new List<TextSearchResult>
        {
            new() { Text = "Document A", Score = 0.9 },
            new() { Text = "Document B", Score = 0.7 },
            new() { Text = "Document C", Score = 0.5 }
        };

        var graded = await grader.GradeAsync("test query", results);

        graded.Count.ShouldBe(3);
        graded.ShouldAllBe(g => g.IsRelevant);
        graded[0].RelevanceScore.ShouldBe(0.9);
        graded[1].RelevanceScore.ShouldBe(0.7);
        graded[2].RelevanceScore.ShouldBe(0.5);
        graded[0].Result.ShouldBe(results[0]);
        graded[1].Result.ShouldBe(results[1]);
        graded[2].Result.ShouldBe(results[2]);
    }

    [Fact]
    public async Task NoOpRelevanceGrader_EmptyResults_ReturnsEmpty()
    {
        var grader = new NoOpRelevanceGrader();

        var graded = await grader.GradeAsync("query", []);

        graded.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoOpRelevanceGrader_NullScore_DefaultsToOne()
    {
        var grader = new NoOpRelevanceGrader();
        var results = new List<TextSearchResult>
        {
            new() { Text = "No score result", Score = null }
        };

        var graded = await grader.GradeAsync("query", results);

        graded.Count.ShouldBe(1);
        graded[0].RelevanceScore.ShouldBe(1.0);
        graded[0].IsRelevant.ShouldBeTrue();
    }

    #endregion

    #region LlmQueryRewriter Tests

    [Fact]
    public async Task LlmQueryRewriter_RewritesQuery()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var rewrittenText = "semantic search machine learning definition concepts";

        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, rewrittenText)));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmQueryRewriter>>();

        var rewriter = new LlmQueryRewriter(mockFactory.Object, options, logger.Object);

        // Act
        var result = await rewriter.RewriteAsync("what is ML?");

        // Assert
        result.ShouldBe(rewrittenText);
        mockChatClient.Verify(c => c.GetResponseAsync(
            It.Is<IList<ChatMessage>>(msgs =>
                msgs.Count == 2 &&
                msgs[0].Role == ChatRole.System &&
                msgs[1].Role == ChatRole.User &&
                msgs[1].Text == "what is ML?"),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LlmQueryRewriter_OnError_ReturnsOriginal()
    {
        // Arrange
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("LLM service unavailable"));

        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmQueryRewriter>>();

        var rewriter = new LlmQueryRewriter(mockFactory.Object, options, logger.Object);

        // Act
        var result = await rewriter.RewriteAsync("original query");

        // Assert - fail-open: 返回原始查询
        result.ShouldBe("original query");
    }

    [Fact]
    public async Task LlmQueryRewriter_EmptyResponse_ReturnsOriginal()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmQueryRewriter>>();

        var rewriter = new LlmQueryRewriter(mockFactory.Object, options, logger.Object);

        // Act
        var result = await rewriter.RewriteAsync("test query");

        // Assert - 空响应回退到原始查询
        result.ShouldBe("test query");
    }

    [Fact]
    public async Task LlmQueryRewriter_WhitespaceQuery_ReturnsUnchanged()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmQueryRewriter>>();

        var rewriter = new LlmQueryRewriter(mockFactory.Object, options, logger.Object);

        var result = await rewriter.RewriteAsync("   ");

        result.ShouldBe("   ");
        // 不应调用 LLM
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    #endregion

    #region LlmRelevanceGrader Tests

    [Fact]
    public async Task LlmRelevanceGrader_GradesResults()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        // 第一个结果相关，第二个不相关
        var callCount = 0;
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var response = Interlocked.Increment(ref callCount) == 1 ? "YES" : "NO";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
            });

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmRelevanceGrader>>();

        var grader = new LlmRelevanceGrader(mockFactory.Object, options, logger.Object);

        var results = new List<TextSearchResult>
        {
            new() { Text = "Relevant document about ML", Score = 0.9 },
            new() { Text = "Irrelevant document about cooking", Score = 0.3 }
        };

        // Act
        var graded = await grader.GradeAsync("machine learning", results);

        // Assert
        graded.Count.ShouldBe(2);
        // 注意：并行执行，顺序可能不同，但结果集一致
        var relevant = graded.Where(g => g.IsRelevant).ToList();
        var irrelevant = graded.Where(g => !g.IsRelevant).ToList();
        relevant.Count.ShouldBe(1);
        irrelevant.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LlmRelevanceGrader_OnError_MarksRelevant()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM service error"));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmRelevanceGrader>>();

        var grader = new LlmRelevanceGrader(mockFactory.Object, options, logger.Object);

        var results = new List<TextSearchResult>
        {
            new() { Text = "Some document", Score = 0.8 }
        };

        // Act
        var graded = await grader.GradeAsync("test query", results);

        // Assert - fail-open: 错误时标记为相关
        graded.Count.ShouldBe(1);
        graded[0].IsRelevant.ShouldBeTrue();
        graded[0].RelevanceScore.ShouldBe(0.8);
    }

    [Fact]
    public async Task LlmRelevanceGrader_EmptyResults_ReturnsEmpty()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var options = MsOptions.Create(new AIRagOptions());
        var logger = new Mock<ILogger<LlmRelevanceGrader>>();

        var grader = new LlmRelevanceGrader(mockFactory.Object, options, logger.Object);

        var graded = await grader.GradeAsync("query", []);

        graded.ShouldBeEmpty();
        // 不应调用 LLM
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    #endregion

    #region GradedResult Tests

    [Fact]
    public void GradedResult_DefaultProperties()
    {
        var graded = new GradedResult();

        graded.Result.ShouldBeNull();
        graded.RelevanceScore.ShouldBe(0.0);
        graded.IsRelevant.ShouldBeFalse();
    }

    #endregion
}
