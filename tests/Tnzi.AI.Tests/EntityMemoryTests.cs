using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Infrastructure.Memory;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests;

/// <summary>
/// 实体记忆系统单元测试
/// </summary>
public class EntityMemoryTests
{
    #region EntityMemoryEntry

    [Fact]
    public void EntityMemoryEntry_DefaultProperties()
    {
        var entry = new EntityMemoryEntry();

        entry.EntityName.ShouldBe(string.Empty);
        entry.EntityType.ShouldBe(string.Empty);
        entry.Properties.ShouldNotBeNull();
        entry.Properties.ShouldBeEmpty();
        entry.LastMentioned.ShouldBe(default);
        entry.MentionCount.ShouldBe(0);
        entry.UserId.ShouldBeNull();
    }

    #endregion

    #region LlmEntityExtractor

    [Fact]
    public async Task LlmEntityExtractor_ExtractsEntities()
    {
        // Arrange: 模拟 IChatClient 返回有效 JSON
        var jsonResponse = """
            [
                {"name": "Microsoft", "type": "organization", "properties": {"industry": "technology"}},
                {"name": "Beijing", "type": "location", "properties": {"country": "China"}}
            ]
            """;

        var mockChatClient = CreateMockChatClient(jsonResponse);
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var logger = new Mock<ILogger<LlmEntityExtractor>>().Object;
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, logger);

        // Act
        var entities = await extractor.ExtractAsync("I work at Microsoft in Beijing");

        // Assert
        entities.Count.ShouldBe(2);

        entities[0].EntityName.ShouldBe("Microsoft");
        entities[0].EntityType.ShouldBe("organization");
        entities[0].Properties["industry"].ShouldBe("technology");

        entities[1].EntityName.ShouldBe("Beijing");
        entities[1].EntityType.ShouldBe("location");
        entities[1].Properties["country"].ShouldBe("China");
    }

    [Fact]
    public async Task LlmEntityExtractor_WithMarkdownCodeBlock_ExtractsEntities()
    {
        // Arrange: LLM 在 markdown 代码块中返回 JSON
        var jsonResponse = """
            ```json
            [{"name": "Alice", "type": "person", "properties": {}}]
            ```
            """;

        var mockChatClient = CreateMockChatClient(jsonResponse);
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var logger = new Mock<ILogger<LlmEntityExtractor>>().Object;
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, logger);

        // Act
        var entities = await extractor.ExtractAsync("Alice called me yesterday");

        // Assert
        entities.Count.ShouldBe(1);
        entities[0].EntityName.ShouldBe("Alice");
        entities[0].EntityType.ShouldBe("person");
    }

    [Fact]
    public async Task LlmEntityExtractor_OnError_ReturnsEmpty()
    {
        // Arrange: IChatClientFactory 抛出异常
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Provider not configured"));

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var logger = new Mock<ILogger<LlmEntityExtractor>>().Object;
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, logger);

        // Act
        var entities = await extractor.ExtractAsync("Some text to extract from");

        // Assert: 不抛异常，返回空列表
        entities.ShouldNotBeNull();
        entities.ShouldBeEmpty();
    }

    [Fact]
    public async Task LlmEntityExtractor_EmptyText_ReturnsEmpty()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var logger = new Mock<ILogger<LlmEntityExtractor>>().Object;
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, logger);

        var entities = await extractor.ExtractAsync("");

        entities.ShouldBeEmpty();
    }

    #endregion

    #region EntityMemoryContextProvider

    [Fact]
    public async Task EntityMemoryContextProvider_InjectsEntities()
    {
        // Arrange
        var mockStore = new Mock<IEntityMemoryStore>();
        mockStore.Setup(s => s.GetRelevantEntitiesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityMemoryEntry>
            {
                new()
                {
                    EntityName = "John",
                    EntityType = "person",
                    Properties = new Dictionary<string, string> { { "role", "developer" } },
                    MentionCount = 5,
                    LastMentioned = DateTime.UtcNow
                },
                new()
                {
                    EntityName = "Contoso",
                    EntityType = "organization",
                    Properties = new Dictionary<string, string>(),
                    MentionCount = 3,
                    LastMentioned = DateTime.UtcNow
                }
            });

        var mockExtractor = CreateMockExtractor([]);
        var options = new EntityMemoryOptions { Enabled = true, MaxEntitiesPerContext = 20 };
        var logger = new Mock<ILogger<EntityMemoryContextProvider>>().Object;

        var provider = new EntityMemoryContextProvider(mockStore.Object, options, mockExtractor, logger);

        // Act
        var injection = await provider.GetContextAsync([]);

        // Assert
        injection.HasContent.ShouldBeTrue();
        injection.Messages.ShouldNotBeNull();
        injection.Messages.Count.ShouldBe(1);

        var messageText = injection.Messages[0].Text!;
        messageText.ShouldContain("John");
        messageText.ShouldContain("person");
        messageText.ShouldContain("role: developer");
        messageText.ShouldContain("Contoso");
        messageText.ShouldContain("organization");
    }

    [Fact]
    public async Task EntityMemoryContextProvider_NoEntities_ReturnsEmpty()
    {
        // Arrange
        var mockStore = new Mock<IEntityMemoryStore>();
        mockStore.Setup(s => s.GetRelevantEntitiesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityMemoryEntry>());

        var mockExtractor = CreateMockExtractor([]);
        var options = new EntityMemoryOptions { Enabled = true };
        var logger = new Mock<ILogger<EntityMemoryContextProvider>>().Object;

        var provider = new EntityMemoryContextProvider(mockStore.Object, options, mockExtractor, logger);

        // Act
        var injection = await provider.GetContextAsync([]);

        // Assert
        injection.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task EntityMemoryContextProvider_ExtractsOnCompleted()
    {
        // Arrange
        var mockStore = new Mock<IEntityMemoryStore>();
        mockStore.Setup(s => s.GetRelevantEntitiesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityMemoryEntry>());

        var extractedEntities = new List<EntityMemoryEntry>
        {
            new() { EntityName = "Alice", EntityType = "person" }
        };
        var mockExtractor = CreateMockExtractor(extractedEntities);

        var options = new EntityMemoryOptions { Enabled = true };
        var logger = new Mock<ILogger<EntityMemoryContextProvider>>().Object;

        var provider = new EntityMemoryContextProvider(mockStore.Object, options, mockExtractor, logger);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Tell me about Alice"),
            new(ChatRole.Assistant, "Alice is a software engineer at Contoso.")
        };

        // Act
        await provider.OnCompletedAsync(messages);

        // Assert: 验证 UpsertEntitiesAsync 被调用，且实体列表不为空
        mockStore.Verify(s => s.UpsertEntitiesAsync(
            It.Is<IEnumerable<EntityMemoryEntry>>(entries => entries.Any(e => e.EntityName == "Alice")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EntityMemoryContextProvider_UsesAgentIdFromExecutionContext()
    {
        var requestAgentId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                AgentId = requestAgentId
            }
        };

        try
        {
            var mockStore = new Mock<IEntityMemoryStore>();
            mockStore.Setup(s => s.GetRelevantEntitiesAsync(
                    It.IsAny<Guid?>(),
                    requestAgentId,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<EntityMemoryEntry>());

            var provider = new EntityMemoryContextProvider(
                mockStore.Object,
                new EntityMemoryOptions { Enabled = true },
                CreateMockExtractor([]),
                Mock.Of<ILogger<EntityMemoryContextProvider>>(),
                executionContextAccessor: accessor);

            await provider.GetContextAsync([]);

            mockStore.VerifyAll();
        }
        finally
        {
            accessor.CurrentRequest = null;
        }
    }

    #endregion

    #region EntityMemoryOptions

    [Fact]
    public void EntityMemoryOptions_Defaults()
    {
        var options = new EntityMemoryOptions();

        options.Enabled.ShouldBeFalse();
        options.MaxEntitiesPerContext.ShouldBe(30);
        options.ExtractionProvider.ShouldBeNull();
        options.ExtractionModel.ShouldBeNull();
    }

    #endregion

    #region EntityMemory Entity

    [Fact]
    public void EntityMemory_DefaultProperties()
    {
        var entity = new EntityMemory();

        entity.EntityName.ShouldBe(string.Empty);
        entity.EntityType.ShouldBe(string.Empty);
        entity.Properties.ShouldBe("{}");
        entity.LastMentioned.ShouldBe(default);
        entity.MentionCount.ShouldBe(0);
        entity.UserId.ShouldBeNull();
        entity.AgentId.ShouldBeNull();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 创建返回指定文本的模拟 IChatClient
    /// </summary>
    private static IChatClient CreateMockChatClient(string responseText)
    {
        var mockClient = new Mock<IChatClient>();

        var chatMessage = new ChatMessage(ChatRole.Assistant, responseText);
        var chatResponse = new ChatResponse(chatMessage);

        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        return mockClient.Object;
    }

    /// <summary>
    /// 创建返回指定实体列表的模拟 LlmEntityExtractor
    /// </summary>
    private static LlmEntityExtractor CreateMockExtractor(List<EntityMemoryEntry> entities)
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var jsonResponse = JsonSerializer.Serialize(entities.Select(e => new
        {
            name = e.EntityName,
            type = e.EntityType,
            properties = e.Properties
        }));

        var mockClient = CreateMockChatClient(jsonResponse);
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockClient);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var logger = new Mock<ILogger<LlmEntityExtractor>>().Object;
        return new LlmEntityExtractor(mockFactory.Object, options, logger);
    }

    #endregion
}
