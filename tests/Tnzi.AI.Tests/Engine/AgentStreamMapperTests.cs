namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// AgentStreamMapper 参数化测试 - 验证 BuildStreamEvent / TryMapFailure 与原始三处实现完全等价。
/// </summary>
public class AgentStreamMapperTests
{
    // -------------------------------------------------------------------------
    // BuildStreamEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildStreamEvent_NullPayload_ReturnsNull()
    {
        var chunk = new AgentStreamChunk(); // no payload fields set
        var result = AgentStreamMapper.BuildStreamEvent(chunk, Guid.NewGuid(), ErrorCodes.AgentRunFailed);
        result.ShouldBeNull();
    }

    [Fact]
    public void BuildStreamEvent_WithText_ReturnsDeltaEvent()
    {
        var threadId = Guid.NewGuid();
        var chunk = new AgentStreamChunk { Text = "hello" };

        var result = AgentStreamMapper.BuildStreamEvent(chunk, threadId, ErrorCodes.AgentRunFailed);

        result.ShouldNotBeNull();
        result!.Delta.ShouldBe("hello");
        result.ThreadId.ShouldBe(threadId);
        result.IsError.ShouldBeFalse();
        result.ErrorCode.ShouldBeNull();
    }

    [Theory]
    [InlineData(ErrorCodes.AgentRunFailed)]
    [InlineData(ErrorCodes.ChatFailed)]
    public void BuildStreamEvent_WithError_SetsDefaultErrorCode(string defaultErrorCode)
    {
        var chunk = new AgentStreamChunk
        {
            Text = "err",
            Error = "something went wrong",
            FinishReason = FinishReasons.Error
        };

        var result = AgentStreamMapper.BuildStreamEvent(chunk, null, defaultErrorCode);

        result.ShouldNotBeNull();
        result!.IsError.ShouldBeTrue();
        result.ErrorMessage.ShouldBe("something went wrong");
        result.ErrorCode.ShouldBe(defaultErrorCode);
    }

    [Theory]
    [InlineData(ErrorCodes.AgentRunFailed)]
    [InlineData(ErrorCodes.ChatFailed)]
    public void BuildStreamEvent_GuardrailError_SetsGuardrailErrorCode(string defaultErrorCode)
    {
        var chunk = new AgentStreamChunk
        {
            Text = "blocked",
            Error = "guardrail triggered",
            FinishReason = FinishReasons.GuardrailRejected
        };

        var result = AgentStreamMapper.BuildStreamEvent(chunk, null, defaultErrorCode);

        result.ShouldNotBeNull();
        result!.IsError.ShouldBeTrue();
        // Guardrail always wins regardless of defaultErrorCode
        result.ErrorCode.ShouldBe(ErrorCodes.GuardrailRejected);
    }

    [Fact]
    public void BuildStreamEvent_WithToolCall_SetsIsToolCall()
    {
        var chunk = new AgentStreamChunk { IsToolCall = true };
        var result = AgentStreamMapper.BuildStreamEvent(chunk, null, ErrorCodes.AgentRunFailed);

        result.ShouldNotBeNull();
        result!.IsToolCall.ShouldBeTrue();
    }

    [Fact]
    public void BuildStreamEvent_WithSuggestionsAndArtifacts_PropagatesFields()
    {
        var suggestions = new List<string> { "s1", "s2" };
        var chunk = new AgentStreamChunk { Suggestions = suggestions };

        var result = AgentStreamMapper.BuildStreamEvent(chunk, null, ErrorCodes.AgentRunFailed);

        result.ShouldNotBeNull();
        result!.Suggestions.ShouldBe(suggestions);
    }

    // -------------------------------------------------------------------------
    // TryMapFailure
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(FinishReasons.QuotaExceeded, 429, ErrorCodes.QuotaExceeded, ErrorCodes.AgentRunFailed)]
    [InlineData(FinishReasons.QuotaExceeded, 429, ErrorCodes.QuotaExceeded, ErrorCodes.ChatFailed)]
    [InlineData(FinishReasons.GuardrailRejected, 400, ErrorCodes.GuardrailRejected, ErrorCodes.AgentRunFailed)]
    [InlineData(FinishReasons.GuardrailRejected, 400, ErrorCodes.GuardrailRejected, ErrorCodes.ChatFailed)]
    public void TryMapFailure_FixedErrorCodeReasons_AlwaysReturnsSameCode(
        string finishReason, int expectedStatus, string expectedCode, string failureCode)
    {
        var result = new AgentRunResult { Response = string.Empty, FinishReason = finishReason };
        var mapped = AgentStreamMapper.TryMapFailure(result, failureCode, out var status, out var code);

        mapped.ShouldBeTrue();
        status.ShouldBe(expectedStatus);
        code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(FinishReasons.Rejected, 400, ErrorCodes.AgentRunFailed)]
    [InlineData(FinishReasons.MaxHandoffs, 500, ErrorCodes.AgentRunFailed)]
    [InlineData(FinishReasons.Error, 500, ErrorCodes.AgentRunFailed)]
    [InlineData(FinishReasons.Failed, 500, ErrorCodes.AgentRunFailed)]
    public void TryMapFailure_AgentRunFailed_VariantReasons(string finishReason, int expectedStatus, string expectedCode)
    {
        var result = new AgentRunResult { Response = string.Empty, FinishReason = finishReason };
        var mapped = AgentStreamMapper.TryMapFailure(result, ErrorCodes.AgentRunFailed, out var status, out var code);

        mapped.ShouldBeTrue();
        status.ShouldBe(expectedStatus);
        code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(FinishReasons.Rejected, 400, ErrorCodes.ChatFailed)]
    [InlineData(FinishReasons.MaxHandoffs, 500, ErrorCodes.ChatFailed)]
    [InlineData(FinishReasons.Error, 500, ErrorCodes.ChatFailed)]
    [InlineData(FinishReasons.Failed, 500, ErrorCodes.ChatFailed)]
    public void TryMapFailure_ChatFailed_VariantReasons(string finishReason, int expectedStatus, string expectedCode)
    {
        var result = new AgentRunResult { Response = string.Empty, FinishReason = finishReason };
        var mapped = AgentStreamMapper.TryMapFailure(result, ErrorCodes.ChatFailed, out var status, out var code);

        mapped.ShouldBeTrue();
        status.ShouldBe(expectedStatus);
        code.ShouldBe(expectedCode);
    }

    [Fact]
    public void TryMapFailure_SuccessfulFinish_ReturnsFalse()
    {
        var result = new AgentRunResult { Response = string.Empty, FinishReason = FinishReasons.Stop };
        var mapped = AgentStreamMapper.TryMapFailure(result, ErrorCodes.AgentRunFailed, out var status, out var code);

        mapped.ShouldBeFalse();
        status.ShouldBe(0);
        code.ShouldBe(string.Empty);
    }

    [Fact]
    public void TryMapFailure_NullFinishReason_ReturnsFalse()
    {
        var result = new AgentRunResult { Response = string.Empty, FinishReason = null };
        var mapped = AgentStreamMapper.TryMapFailure(result, ErrorCodes.AgentRunFailed, out var status, out var code);

        mapped.ShouldBeFalse();
        status.ShouldBe(0);
        code.ShouldBe(string.Empty);
    }
}
