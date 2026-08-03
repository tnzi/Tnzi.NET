namespace Tnzi.AI.Tests;

/// <summary>
/// DefaultAgentEvaluator 单元测试
/// </summary>
public class AgentEvaluatorTests
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IRepository<EvaluationRun, Guid>> _repositoryMock;
    private readonly Mock<IAiUtility> _aiUtilityMock;
    private readonly DefaultAgentEvaluator _evaluator;

    public AgentEvaluatorTests()
    {
        _chatServiceMock = new Mock<IChatService>();
        _repositoryMock = new Mock<IRepository<EvaluationRun, Guid>>();
        _aiUtilityMock = new Mock<IAiUtility>();
        var loggerMock = new Mock<ILogger<DefaultAgentEvaluator>>();

        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 默认 IAiUtility 返回 null（LLM 不可用）→ 评估器回退到字符串匹配。
        // LLM-as-judge 路径由专门的测试通过 setup 覆盖。
        _aiUtilityMock.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _evaluator = new DefaultAgentEvaluator(_chatServiceMock.Object, _repositoryMock.Object, _aiUtilityMock.Object, loggerMock.Object);
    }

    [Fact]
    public async Task EvaluateAsync_ExactMatch_ReturnsPassed()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "What is 2+2?",
            ExpectedOutput = "4"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "4" }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Passed.ShouldBeTrue();
        result.Score.ShouldBe(1.0);
        result.Reason.ShouldBe("Exact match");
        result.ActualOutput.ShouldBe("4");
        result.CaseId.ShouldBe(evaluationCase.CaseId);
    }

    [Fact]
    public async Task EvaluateAsync_ContainsMatch_ReturnsPassed()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "What is the capital of France?",
            ExpectedOutput = "Paris"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "The capital of France is Paris." }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Passed.ShouldBeTrue();
        result.Score.ShouldBe(0.8);
        // LLM unavailable here (mock returns null) → string-match fallback
        result.Reason.ShouldBe("Contains expected output (string-match fallback)");
    }

    [Fact]
    public async Task EvaluateAsync_NoMatch_ReturnsFailed()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "What is 2+2?",
            ExpectedOutput = "4"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "The answer is five." }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Passed.ShouldBeFalse();
        result.Score.ShouldBe(0.0);
        // LLM unavailable here (mock returns null) → string-match fallback
        result.Reason.ShouldBe("Output does not match expected (string-match fallback)");
    }

    [Fact]
    public async Task EvaluateAsync_NoExpectedOutput_PassesWhenHasOutput()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "Tell me something",
            ExpectedOutput = null
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "Here is something." }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Passed.ShouldBeTrue();
        result.Score.ShouldBe(1.0);
        result.Reason.ShouldBe("Output generated (no expected output specified)");
    }

    [Fact]
    public async Task EvaluateAsync_ChatServiceFails_ReturnsFailed()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "test",
            ExpectedOutput = "expected"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Chat service error"));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Passed.ShouldBeFalse();
        result.Score.ShouldBe(0.0);
        result.Reason.ShouldStartWith("Evaluation failed:");
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task EvaluateAsync_RecordsDuration()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "test",
            ExpectedOutput = "test"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "test" }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert
        result.Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task EvaluateBatchAsync_MultipleCases_ReturnsSummary()
    {
        // Arrange
        var cases = new List<EvaluationCase>
        {
            new() { Input = "Q1", ExpectedOutput = "A1" },
            new() { Input = "Q2", ExpectedOutput = "A2" },
            new() { Input = "Q3", ExpectedOutput = "A3" }
        };

        _chatServiceMock.SetupSequence(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "A1" }))   // 精确匹配
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "Wrong" })) // 不匹配
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "A3" }));  // 精确匹配

        // Act
        var summary = await _evaluator.EvaluateBatchAsync(cases);

        // Assert
        summary.TotalCases.ShouldBe(3);
        summary.PassedCases.ShouldBe(2);
        summary.Results.Count.ShouldBe(3);
        summary.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task EvaluateBatchAsync_AllPass_CorrectStatistics()
    {
        // Arrange
        var cases = new List<EvaluationCase>
        {
            new() { Input = "Q1", ExpectedOutput = "A1" },
            new() { Input = "Q2", ExpectedOutput = "A2" }
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.Is<ChatRequestDto>(r => r.Message == "Q1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "A1" }));
        _chatServiceMock.Setup(s => s.ChatAsync(It.Is<ChatRequestDto>(r => r.Message == "Q2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "A2" }));

        // Act
        var summary = await _evaluator.EvaluateBatchAsync(cases);

        // Assert
        summary.PassRate.ShouldBe(1.0);
        summary.AverageScore.ShouldBe(1.0);
        summary.PassedCases.ShouldBe(2);
    }

    [Fact]
    public async Task EvaluateBatchAsync_AllFail_CorrectStatistics()
    {
        // Arrange
        var cases = new List<EvaluationCase>
        {
            new() { Input = "Q1", ExpectedOutput = "A1" },
            new() { Input = "Q2", ExpectedOutput = "A2" }
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "Wrong answer" }));

        // Act
        var summary = await _evaluator.EvaluateBatchAsync(cases);

        // Assert
        summary.PassRate.ShouldBe(0.0);
        summary.AverageScore.ShouldBe(0.0);
        summary.PassedCases.ShouldBe(0);
    }

    [Fact]
    public async Task EvaluateBatchAsync_PersistsEvaluationRun()
    {
        // Arrange
        var cases = new List<EvaluationCase>
        {
            new() { Input = "Q1", ExpectedOutput = "A1" }
        };

        // 使用 Callback 捕获 Insert 时的状态快照（对象会被后续修改）
        string? insertStatus = null;
        int? insertCaseCount = null;
        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<EvaluationRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvaluationRun, CancellationToken>((run, _) =>
            {
                insertStatus = run.Status.ToString();
                insertCaseCount = run.CaseCount;
            })
            .Returns(Task.CompletedTask);

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "A1" }));

        // Act
        await _evaluator.EvaluateBatchAsync(cases);

        // Assert - 验证 Insert 时状态为 running
        insertStatus.ShouldBe("Running");
        insertCaseCount.ShouldBe(1);

        // Assert - 验证 Update 时状态为 completed（对象已被修改，直接验证最终状态）
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<EvaluationRun>(run =>
            run.Status == EvaluationRunStatus.Completed && run.PassedCount == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EvaluationSummary_EmptyResults_ReturnsZeroStats()
    {
        // Arrange
        var summary = new EvaluationSummary
        {
            Results = [],
            TotalCases = 0,
            PassedCases = 0
        };

        // Assert
        summary.PassRate.ShouldBe(0.0);
        summary.AverageScore.ShouldBe(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_CaseInsensitiveMatch_ReturnsPassed()
    {
        // Arrange
        var evaluationCase = new EvaluationCase
        {
            Input = "What is HTTP?",
            ExpectedOutput = "hypertext transfer protocol"
        };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "Hypertext Transfer Protocol" }));

        // Act
        var result = await _evaluator.EvaluateAsync(evaluationCase);

        // Assert - 忽略大小写的精确匹配
        result.Passed.ShouldBeTrue();
        result.Score.ShouldBe(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_SemanticMatch_UsesLlmJudgeScore()
    {
        // Non-exact but semantically related → LLM judge scores it (not string match)
        var evaluationCase = new EvaluationCase { Input = "Capital of France?", ExpectedOutput = "Paris" };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "It is the City of Light." }));
        _aiUtilityMock.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"score":0.92,"pass":true,"reason":"refers to Paris"}""");

        var result = await _evaluator.EvaluateAsync(evaluationCase);

        result.Passed.ShouldBeTrue();
        result.Score.ShouldBe(0.92);
        result.Reason!.ShouldContain("LLM judge");
    }

    [Fact]
    public async Task EvaluateAsync_LlmJudgeMarkdownWrapped_IsParsed()
    {
        // LLM responses often wrap JSON in markdown fences - must still parse
        var evaluationCase = new EvaluationCase { Input = "Q", ExpectedOutput = "Paris" };

        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "unrelated wording" }));
        _aiUtilityMock.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n{\"score\":0.2,\"pass\":false,\"reason\":\"unrelated\"}\n```");

        var result = await _evaluator.EvaluateAsync(evaluationCase);

        result.Passed.ShouldBeFalse();
        result.Score.ShouldBe(0.2);
    }

    [Fact]
    public async Task EvaluateAsync_ExactMatch_DoesNotCallLlmJudge()
    {
        // Exact match is a zero-cost fast path - LLM judge must not be invoked
        var evaluationCase = new EvaluationCase { Input = "What is 2+2?", ExpectedOutput = "4" };
        _chatServiceMock.Setup(s => s.ChatAsync(It.IsAny<ChatRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatResponseDto>.Success(new ChatResponseDto { Content = "4" }));

        await _evaluator.EvaluateAsync(evaluationCase);

        _aiUtilityMock.Verify(u => u.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
