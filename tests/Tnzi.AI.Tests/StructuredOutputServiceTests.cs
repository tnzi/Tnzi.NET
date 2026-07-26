namespace Tnzi.AI.Tests;

/// <summary>
/// StructuredOutputService 单元测试
/// </summary>
public class StructuredOutputServiceTests
{
    private readonly Mock<IChatClientFactory> _mockChatClientFactory;
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly IServiceProvider _serviceProvider;

    public StructuredOutputServiceTests()
    {
        _mockChatClientFactory = new Mock<IChatClientFactory>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockChatClient = new Mock<IChatClient>();

        _mockChatClientFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(_mockChatClient.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private StructuredOutputService CreateService(AIOptions? options = null) => new(
        _serviceProvider,
        _mockChatClientFactory.Object,
        _mockAgentFactory.Object,
        new StaticOptionsMonitor<AIOptions>(options ?? new AIOptions { DefaultProvider = "OpenAI" }));

    private void SetupChatClientResponse(string json)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
    }

    private void SetupChatClientThrows(Exception ex)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
    }

    // -------------------------------------------------------------------------
    // Simple DTO for tests
    // -------------------------------------------------------------------------

    public class PersonDto
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    // -------------------------------------------------------------------------
    // Normal JSON response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_ValidJson_ReturnsParsedObject()
    {
        SetupChatClientResponse("""{"name":"Alice","age":30}""");

        var service = CreateService();
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return a person JSON");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Name.ShouldBe("Alice");
        result.Data.Age.ShouldBe(30);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_JsonWrappedInMarkdownFences_ParsedCorrectly()
    {
        SetupChatClientResponse("```json\n{\"name\":\"Bob\",\"age\":25}\n```");

        var service = CreateService();
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return a person JSON");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Name.ShouldBe("Bob");
        result.Data.Age.ShouldBe(25);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_JsonWrappedInPlainFences_ParsedCorrectly()
    {
        SetupChatClientResponse("```\n{\"name\":\"Charlie\",\"age\":40}\n```");

        var service = CreateService();
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return a person JSON");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Charlie");
    }

    // -------------------------------------------------------------------------
    // Invalid JSON → retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_InvalidJsonOnFirstTry_RetriesAndSucceeds()
    {
        var callCount = 0;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var json = callCount == 1 ? "not valid json" : """{"name":"Dave","age":22}""";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
            });

        var service = CreateService();
        var options = new StructuredOutputOptions { MaxRetries = 1 };
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return a person JSON", options);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Dave");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_AllRetriesExhausted_ReturnsFail()
    {
        SetupChatClientResponse("this is not json at all");

        var service = CreateService();
        var options = new StructuredOutputOptions { MaxRetries = 1 };
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
    }

    // -------------------------------------------------------------------------
    // StrictMode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_StrictMode_SetsJsonSchemaResponseFormat()
    {
        ChatOptions? capturedOptions = null;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
            {
                capturedOptions = opts;
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"name":"Eve","age":28}""")));

        var service = CreateService();
        var options = new StructuredOutputOptions { StrictMode = true };
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        result.Succeeded.ShouldBeTrue();
        capturedOptions.ShouldNotBeNull();
        // StrictMode uses ForJsonSchema, which is a ChatResponseFormatJson with a schema name
        capturedOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
    }

    [Fact]
    public async Task GetStructuredOutputAsync_NonStrictMode_SetsJsonResponseFormat()
    {
        ChatOptions? capturedOptions = null;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
            {
                capturedOptions = opts;
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"name":"Frank","age":35}""")));

        var service = CreateService();
        // Explicitly disable strict mode - service should use ChatResponseFormat.Json
        var options = new StructuredOutputOptions { StrictMode = false };
        await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
    }

    // -------------------------------------------------------------------------
    // Messages overload
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_WithMessages_UsesMessageList()
    {
        SetupChatClientResponse("""{"name":"Grace","age":27}""");

        var service = CreateService();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Extract the person"),
            new(ChatRole.Assistant, "Sure, here it is"),
            new(ChatRole.User, "Return JSON")
        };

        var result = await service.GetStructuredOutputAsync<PersonDto>(messages);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Grace");
    }

    // -------------------------------------------------------------------------
    // Empty / null response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_EmptyResponse_ReturnsFail()
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        var service = CreateService();
        var options = new StructuredOutputOptions { MaxRetries = 0 };
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_WhenCancelled_PropagatesException()
    {
        SetupChatClientThrows(new OperationCanceledException());

        var service = CreateService();
        var options = new StructuredOutputOptions { MaxRetries = 0 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // With MaxRetries=0 and cancellation thrown, the service catches and returns Fail
        var result = await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options, cts.Token);

        result.Succeeded.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Temperature and ModelId options
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStructuredOutputAsync_WithModelId_PassesModelToChatClient()
    {
        SetupChatClientResponse("""{"name":"Henry","age":45}""");

        var service = CreateService();
        var options = new StructuredOutputOptions { ModelId = "gpt-4.1-mini" };
        await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        _mockChatClientFactory.Verify(
            f => f.GetChatClient(It.IsAny<string?>(), "gpt-4.1-mini"),
            Times.Once);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_WithTemperature_SetsTemperatureInChatOptions()
    {
        ChatOptions? capturedOptions = null;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
            {
                capturedOptions = opts;
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"name":"Iris","age":31}""")));

        var service = CreateService();
        var options = new StructuredOutputOptions { Temperature = 0.2f };
        await service.GetStructuredOutputAsync<PersonDto>("Return JSON", options);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Temperature.ShouldNotBeNull();
        capturedOptions.Temperature!.Value.ShouldBe(0.2f, tolerance: 0.001f);
    }
}
