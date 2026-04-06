using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

public class MemorySideQueryTests
{
    private static MemorySideQuery CreateSideQuery(string? llmResponse, bool shouldThrow = false)
    {
        var aiUtility = new Mock<IAiUtility>();

        if (shouldThrow)
        {
            aiUtility.Setup(u => u.ExecuteAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("LLM unavailable"));
        }
        else
        {
            aiUtility.Setup(u => u.ExecuteAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);
        }

        return new MemorySideQuery(aiUtility.Object, Mock.Of<ILogger<MemorySideQuery>>());
    }

    [Fact]
    public async Task FilterRelevantAsync_ReturnsRelevantIndices()
    {
        var sideQuery = CreateSideQuery("[0, 2]");

        var entries = new List<MemorySnippet>
        {
            new(0, "preference", "User prefers dark mode"),
            new(1, "fact", "User works at Acme Corp"),
            new(2, "preference", "User likes TypeScript"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "What are the user's preferences?");

        result.ShouldNotBeEmpty();
        result.Count.ShouldBe(2);
        result[0].ShouldBe(0);
        result[1].ShouldBe(2);
    }

    [Fact]
    public async Task FilterRelevantAsync_LlmFails_ReturnsAllIndices()
    {
        var sideQuery = CreateSideQuery(null, shouldThrow: true);

        var entries = new List<MemorySnippet>
        {
            new(0, "preference", "User prefers dark mode"),
            new(1, "fact", "User works at Acme Corp"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "anything");

        result.Count.ShouldBe(2);
        result[0].ShouldBe(0);
        result[1].ShouldBe(1);
    }

    [Fact]
    public async Task FilterRelevantAsync_EmptyEntries_ReturnsEmpty()
    {
        var sideQuery = CreateSideQuery("[0]");

        var result = await sideQuery.FilterRelevantAsync([], "anything");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task FilterRelevantAsync_InvalidJson_ReturnsAllIndices()
    {
        var sideQuery = CreateSideQuery("I think entries 0 and 2 are relevant");

        var entries = new List<MemorySnippet>
        {
            new(0, "preference", "User prefers dark mode"),
            new(1, "fact", "User works at Acme Corp"),
            new(2, null, "Some other memory"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "preferences");

        // 没有有效 JSON 数组 → 回退返回所有索引
        result.Count.ShouldBe(3);
    }

    [Fact]
    public async Task FilterRelevantAsync_NullResponse_ReturnsAllIndices()
    {
        var sideQuery = CreateSideQuery(null);

        var entries = new List<MemorySnippet>
        {
            new(0, "fact", "Some memory"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "query");

        result.Count.ShouldBe(1);
        result[0].ShouldBe(0);
    }

    [Fact]
    public async Task FilterRelevantAsync_FiltersOutOfRangeIndices()
    {
        var sideQuery = CreateSideQuery("[0, 5, -1, 1]");

        var entries = new List<MemorySnippet>
        {
            new(0, "fact", "Memory A"),
            new(1, "fact", "Memory B"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "query");

        result.Count.ShouldBe(2);
        result[0].ShouldBe(0);
        result[1].ShouldBe(1);
    }

    [Fact]
    public async Task FilterRelevantAsync_EmptyArray_ReturnsEmpty()
    {
        var sideQuery = CreateSideQuery("[]");

        var entries = new List<MemorySnippet>
        {
            new(0, "fact", "Memory A"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "unrelated query");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task FilterRelevantAsync_JsonWithSurroundingText_ExtractsArray()
    {
        // LLM 可能在 JSON 前后添加说明文字
        var sideQuery = CreateSideQuery("Here are the relevant indices: [1, 0] based on the query.");

        var entries = new List<MemorySnippet>
        {
            new(0, "preference", "User prefers dark mode"),
            new(1, "fact", "User works at Acme Corp"),
        };

        var result = await sideQuery.FilterRelevantAsync(entries, "work info");

        result.Count.ShouldBe(2);
        result[0].ShouldBe(1);
        result[1].ShouldBe(0);
    }

    [Fact]
    public async Task FilterRelevantAsync_MaxTokensSetTo256()
    {
        var aiUtility = new Mock<IAiUtility>();
        aiUtility.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[]")
            .Callback<string, string, AiUtilityCallOptions?, CancellationToken>((_, _, options, _) =>
            {
                options.ShouldNotBeNull();
                options.MaxTokens.ShouldBe(256);
            });

        var sideQuery = new MemorySideQuery(aiUtility.Object);

        var entries = new List<MemorySnippet> { new(0, "fact", "test") };
        await sideQuery.FilterRelevantAsync(entries, "query");

        aiUtility.Verify(u => u.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<AiUtilityCallOptions>(o => o != null && o.MaxTokens == 256),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
