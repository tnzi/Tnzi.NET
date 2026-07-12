namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// B5: GuardrailsOptions.InspectToolArguments — flag ON causes ToolGuardrailMiddleware to
/// serialize tool arguments as Content and pass them to content-inspection providers;
/// flag OFF (default) preserves existing behavior where Content is null.
/// </summary>
public class ToolGuardrailInspectArgumentsTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IOptionsMonitor<AIOptions> CreateOptions(bool enabled = true, bool inspectToolArgs = false)
    {
        var opts = new AIOptions();
        opts.Guardrails.Enabled = enabled;
        opts.Guardrails.InspectToolArguments = inspectToolArgs;
        return new StaticOptionsMonitor<AIOptions>(opts);
    }

    private static ToolExecutionContext CreateToolContext(string toolName = "write_file",
        Dictionary<string, object?>? args = null)
        => new()
        {
            ToolName = toolName,
            CallId = "call_1",
            Arguments = args ?? new Dictionary<string, object?> { ["path"] = "/etc/passwd", ["content"] = "evil@corp.com" },
            CancellationToken = CancellationToken.None
        };

    private static ToolGuardrailMiddleware CreateMiddleware(
        IEnumerable<IGuardrailProvider> providers,
        IOptionsMonitor<AIOptions> options)
        => new(providers, options, NullLogger<ToolGuardrailMiddleware>.Instance);

    // -----------------------------------------------------------------------
    // InspectToolArguments = true
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InspectToolArguments_True_ContentProviderReceivesSerializedArgs()
    {
        // Arrange — a content-inspection provider that rejects when Content is non-null
        GuardrailRequest? captured = null;

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(p => p.Name).Returns("ContentScanner");
        provider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GuardrailRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync((GuardrailRequest req, CancellationToken _) =>
                req.Content != null
                    ? GuardrailDecision.Deny("content_denied", "Bad arg content")
                    : GuardrailDecision.Allow());

        var middleware = CreateMiddleware([provider.Object], CreateOptions(inspectToolArgs: true));

        // Act
        var result = await middleware.InvokeAsync(CreateToolContext(), () => Task.FromResult<object?>("ok"));

        // Assert — Content was set, provider denied based on it
        captured.ShouldNotBeNull();
        captured!.Content.ShouldNotBeNullOrEmpty();
        // Content should be the JSON serialization of the tool arguments
        captured.Content.ShouldContain("path");
        captured.Content.ShouldContain("/etc/passwd");
        // ToolInput is ALWAYS set regardless of the flag
        captured.ToolInput.ShouldNotBeNull();
        // Denial result
        result.ShouldBeOfType<string>().ShouldContain("Bad arg content");
    }

    [Fact]
    public async Task InspectToolArguments_True_ProviderDeniesOnBadArgValue()
    {
        // Simulate a PII-like check: deny if the serialized args contain an email
        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(p => p.Name).Returns("PiiArgScanner");
        provider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GuardrailRequest req, CancellationToken _) =>
                req.Content != null && req.Content.Contains("@")
                    ? GuardrailDecision.Deny("pii_in_args", "Email in tool arguments")
                    : GuardrailDecision.Allow());

        var middleware = CreateMiddleware([provider.Object], CreateOptions(inspectToolArgs: true));
        var nextCalled = false;

        var args = new Dictionary<string, object?> { ["content"] = "send to evil@corp.com" };
        var result = await middleware.InvokeAsync(CreateToolContext(args: args), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("ok");
        });

        nextCalled.ShouldBeFalse();
        result.ShouldBeOfType<string>().ShouldContain("Email in tool arguments");
    }

    // -----------------------------------------------------------------------
    // InspectToolArguments = false (default)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InspectToolArguments_False_ContentIsNullSoContentProviderAllows()
    {
        // Provider that only denies when Content is non-null (content-based check)
        GuardrailRequest? captured = null;

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(p => p.Name).Returns("ContentScanner");
        provider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GuardrailRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync((GuardrailRequest req, CancellationToken _) =>
                req.Content != null
                    ? GuardrailDecision.Deny("content_denied", "Would deny if content present")
                    : GuardrailDecision.Allow());

        // InspectToolArguments defaults to false
        var middleware = CreateMiddleware([provider.Object], CreateOptions(inspectToolArgs: false));
        var nextCalled = false;

        var result = await middleware.InvokeAsync(CreateToolContext(), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("tool-result");
        });

        // Content is null → provider allows → next is called
        captured.ShouldNotBeNull();
        captured!.Content.ShouldBeNull();
        // ToolInput still populated regardless of flag
        captured.ToolInput.ShouldNotBeNull();
        nextCalled.ShouldBeTrue();
        result.ShouldBe("tool-result");
    }

    [Fact]
    public async Task InspectToolArguments_DefaultIsFalse()
    {
        // Confirm the option default is false (not a breaking change)
        var opts = new GuardrailsOptions();
        opts.InspectToolArguments.ShouldBeFalse();
    }
}
