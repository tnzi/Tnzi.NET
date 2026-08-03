namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// B4: InputGuardrailMiddleware - verify that when a guardrail returns a Modified result
/// the EffectiveUserMessage is set on the context so AgentRuntime picks up the redacted text.
/// Covers both the non-streaming (InvokeAsync) and the streaming (InvokeStreamingAsync) paths.
/// </summary>
public class InputGuardrailEffectiveUserMessageTests
{
    private const string TestProvider = "OpenAI";
    private const string TestModel = "gpt-4o";

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static GuardrailRunner CreateRunner(IInputGuardrail guardrail, bool enabled = true)
    {
        var options = new AIOptions { Guardrails = new GuardrailsOptions { Enabled = enabled } };
        return new GuardrailRunner(
            [guardrail],
            Enumerable.Empty<IOutputGuardrail>(),
            new StaticOptionsMonitor<AIOptions>(options),
            NullLogger<GuardrailRunner>.Instance);
    }

    private static InputGuardrailMiddleware CreateMiddleware(IInputGuardrail guardrail, bool enabled = true)
        => new(CreateRunner(guardrail, enabled), NullLogger<InputGuardrailMiddleware>.Instance);

    private static AiMiddlewareContext CreateContext(string userMessage)
        => new()
        {
            Request = new AgentRunRequest { UserMessage = userMessage, UserId = Guid.NewGuid() },
            Agent = AgentResolution.Success(agent: null!, provider: TestProvider, model: TestModel, agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

    // -----------------------------------------------------------------------
    // Non-streaming path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WhenGuardrailModifiesText_SetsEffectiveUserMessage()
    {
        // Arrange - guardrail replaces the raw message with a redacted version
        const string originalMessage = "My phone is 13800138000";
        const string redactedMessage = "My phone is [REDACTED]";

        var guardrail = new Mock<IInputGuardrail>();
        guardrail.Setup(g => g.ValidateAsync(originalMessage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Modified("PiiGuardrail", redactedMessage, "PII redacted"));

        var middleware = CreateMiddleware(guardrail.Object);
        var context = CreateContext(originalMessage);

        string? capturedEffective = null;
        await middleware.InvokeAsync(context, (ctx, _) =>
        {
            capturedEffective = ctx.EffectiveUserMessage;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        // EffectiveUserMessage should carry the redacted text downstream
        capturedEffective.ShouldBe(redactedMessage);
        // Properties bag should NOT carry the old dead key
        context.Properties.ContainsKey("GuardrailModifiedInput").ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WhenGuardrailDoesNotModifyText_EffectiveUserMessageIsNull()
    {
        const string safeMessage = "Hello world";

        var guardrail = new Mock<IInputGuardrail>();
        guardrail.Setup(g => g.ValidateAsync(safeMessage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Allowed());

        var middleware = CreateMiddleware(guardrail.Object);
        var context = CreateContext(safeMessage);

        string? capturedEffective = "sentinel";
        await middleware.InvokeAsync(context, (ctx, _) =>
        {
            capturedEffective = ctx.EffectiveUserMessage;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        // When unmodified, EffectiveUserMessage remains null so AgentRuntime falls back to Request.UserMessage
        capturedEffective.ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // Streaming path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeStreamingAsync_WhenGuardrailModifiesText_SetsEffectiveUserMessage()
    {
        const string originalMessage = "Email: user@example.com";
        const string redactedMessage = "Email: [REDACTED]";

        var guardrail = new Mock<IInputGuardrail>();
        guardrail.Setup(g => g.ValidateAsync(originalMessage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Modified("PiiGuardrail", redactedMessage, "PII redacted"));

        var middleware = CreateMiddleware(guardrail.Object);
        var context = CreateContext(originalMessage);

        string? capturedEffective = null;

        // next delegate captures EffectiveUserMessage at the point the core would run
        async IAsyncEnumerable<AgentStreamChunk> Next(
            AiMiddlewareContext ctx,
            [EnumeratorCancellation] CancellationToken _)
        {
            capturedEffective = ctx.EffectiveUserMessage;
            yield return new AgentStreamChunk { Text = "streaming-ok" };
        }

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, Next))
        {
            chunks.Add(chunk);
        }

        capturedEffective.ShouldBe(redactedMessage);
        chunks.ShouldHaveSingleItem();
        chunks[0].Text.ShouldBe("streaming-ok");
    }

    [Fact]
    public async Task InvokeStreamingAsync_WhenGuardrailDoesNotModifyText_EffectiveUserMessageIsNull()
    {
        const string safeMessage = "safe message";

        var guardrail = new Mock<IInputGuardrail>();
        guardrail.Setup(g => g.ValidateAsync(safeMessage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Allowed());

        var middleware = CreateMiddleware(guardrail.Object);
        var context = CreateContext(safeMessage);

        string? capturedEffective = "sentinel";

        async IAsyncEnumerable<AgentStreamChunk> Next(
            AiMiddlewareContext ctx,
            [EnumeratorCancellation] CancellationToken _)
        {
            capturedEffective = ctx.EffectiveUserMessage;
            yield return new AgentStreamChunk { Text = "ok" };
        }

        await foreach (var _ in middleware.InvokeStreamingAsync(context, Next)) { }

        capturedEffective.ShouldBeNull();
    }
}
