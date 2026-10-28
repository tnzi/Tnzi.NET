using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

public class LlmMemoryConsolidatorTests
{
    private static LlmMemoryConsolidator CreateConsolidator(string llmResponse)
    {
        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponse)));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(chatClient.Object);

        return new LlmMemoryConsolidator(factory.Object, Mock.Of<ILogger<LlmMemoryConsolidator>>());
    }

    [Fact]
    public async Task ConsolidateAsync_ReturnsAdd_WhenNovelMemory()
    {
        var consolidator = CreateConsolidator("""{"action":"add","content":null}""");
        var existing = new List<MemorySearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "User likes Python", Score = 0.3 }
        };

        var result = await consolidator.ConsolidateAsync("User prefers dark theme", existing);

        result.Action.ShouldBe(MemoryAction.Add);
    }

    [Fact]
    public async Task ConsolidateAsync_ReturnsUpdate_WithMergedContent()
    {
        var targetId = Guid.NewGuid();
        var consolidator = CreateConsolidator(
            $$"""{"action":"update","content":"User prefers light theme (changed from dark)","targetId":"{{targetId}}"}""");
        var existing = new List<MemorySearchResult>
        {
            new() { Id = targetId, Content = "User prefers dark theme", Score = 0.9 }
        };

        var result = await consolidator.ConsolidateAsync("User now prefers light theme", existing);

        result.Action.ShouldBe(MemoryAction.Update);
        var updatedContent = result.UpdatedContent;
        updatedContent.ShouldNotBeNull();
        updatedContent.ShouldContain("light theme");
        result.TargetEntryId.ShouldBe(targetId);
    }

    [Fact]
    public async Task ConsolidateAsync_ReturnsDelete()
    {
        var targetId = Guid.NewGuid();
        var consolidator = CreateConsolidator(
            $$"""{"action":"delete","targetId":"{{targetId}}"}""");
        var existing = new List<MemorySearchResult>
        {
            new() { Id = targetId, Content = "outdated info", Score = 0.7 }
        };

        var result = await consolidator.ConsolidateAsync("This info is no longer true", existing);

        result.Action.ShouldBe(MemoryAction.Delete);
        result.TargetEntryId.ShouldBe(targetId);
    }

    [Fact]
    public async Task ConsolidateAsync_ReturnsNoop()
    {
        var consolidator = CreateConsolidator("""{"action":"noop"}""");
        var existing = new List<MemorySearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "User is a developer", Score = 0.9 }
        };

        var result = await consolidator.ConsolidateAsync("User is a developer", existing);

        result.Action.ShouldBe(MemoryAction.Noop);
    }

    [Fact]
    public async Task ConsolidateAsync_FallsBackToAdd_OnLlmError()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("LLM down"));

        var consolidator = new LlmMemoryConsolidator(factory.Object, Mock.Of<ILogger<LlmMemoryConsolidator>>());
        var existing = new List<MemorySearchResult> { new() { Id = Guid.NewGuid(), Content = "existing", Score = 0.5 } };

        var result = await consolidator.ConsolidateAsync("new memory", existing);

        result.Action.ShouldBe(MemoryAction.Add);
    }

    [Fact]
    public async Task ConsolidateAsync_FallsBackToAdd_OnInvalidJson()
    {
        var consolidator = CreateConsolidator("not json at all");

        var result = await consolidator.ConsolidateAsync("new", [new() { Id = Guid.NewGuid(), Content = "old", Score = 0.5 }]);

        result.Action.ShouldBe(MemoryAction.Add);
    }

    [Fact]
    public async Task ConsolidateAsync_FallsBackToAdd_OnEmptyResponse()
    {
        var consolidator = CreateConsolidator("");

        var result = await consolidator.ConsolidateAsync("new", [new() { Id = Guid.NewGuid(), Content = "old", Score = 0.5 }]);

        result.Action.ShouldBe(MemoryAction.Add);
    }

    [Fact]
    public async Task ConsolidateAsync_HandlesMarkdownCodeBlock()
    {
        var consolidator = CreateConsolidator("```json\n{\"action\":\"noop\"}\n```");

        var result = await consolidator.ConsolidateAsync("test", [new() { Id = Guid.NewGuid(), Content = "test", Score = 0.8 }]);

        result.Action.ShouldBe(MemoryAction.Noop);
    }

    /// <summary>
    /// 没点名目标的 UPDATE / DELETE 不替模型猜一条（此前取最高分那条），按新增处理：
    /// 多存一条可以再合并，改写或删错的记忆找不回来。
    /// </summary>
    [Theory]
    [InlineData("""{"action":"update","content":"merged"}""")]
    [InlineData("""{"action":"delete"}""")]
    [InlineData("""{"action":"delete","targetId":null}""")]
    public async Task ConsolidateAsync_UpdateOrDelete_WithoutTargetId_FallsBackToAdd(string llmResponse)
    {
        var consolidator = CreateConsolidator(llmResponse);
        var existing = new List<MemorySearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "low score", Score = 0.3 },
            new() { Id = Guid.NewGuid(), Content = "high score", Score = 0.9 }
        };

        var result = await consolidator.ConsolidateAsync("new", existing);

        result.Action.ShouldBe(MemoryAction.Add);
        result.TargetEntryId.ShouldBeNull();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ConsolidateAsync_TargetIdNotAmongTheCandidates_FallsBackToAdd(string action)
    {
        var consolidator = CreateConsolidator(
            $$"""{"action":"{{action}}","content":"merged","targetId":"{{Guid.NewGuid()}}"}""");
        var existing = new List<MemorySearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "User lives in Toronto", Score = 0.9 }
        };

        var result = await consolidator.ConsolidateAsync("User moved to Vancouver", existing);

        result.Action.ShouldBe(MemoryAction.Add);
        result.TargetEntryId.ShouldBeNull();
    }
}
