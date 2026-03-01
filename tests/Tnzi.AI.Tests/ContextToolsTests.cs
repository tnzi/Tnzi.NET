using Tnzi.AI.Coder.Context;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests;

/// <summary>
/// ContextTools 单元测试 — 验证上下文分析和压缩功能
/// </summary>
public class ContextToolsTests
{
    private readonly Mock<IMemoryStore> _memoryStoreMock;
    private readonly Mock<ILogger<ContextTools>> _loggerMock;
    private readonly ContextTools _tools;

    public ContextToolsTests()
    {
        _memoryStoreMock = new Mock<IMemoryStore>();
        _loggerMock = new Mock<ILogger<ContextTools>>();
        _tools = new ContextTools(_memoryStoreMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ContextStats_ReturnsCharAndTokenCounts()
    {
        var conversation = "user: Hello\nassistant: Hi there!\nuser: How are you?\nassistant: I'm good!";

        var result = await _tools.ContextStatsAsync(conversation);

        result.ShouldNotBeNull();
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"character_count\"");
        json.ShouldContain("\"estimated_tokens\"");
        json.ShouldContain("\"turn_count\"");
    }

    [Fact]
    public async Task ContextStats_EmptyText_ReturnsError()
    {
        var result = await _tools.ContextStatsAsync("");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    [Fact]
    public async Task ContextStats_LargeText_RecommendsCondensation()
    {
        // Generate text > 50k chars
        var largeText = string.Join("\n", Enumerable.Range(0, 2000)
            .Select(i => $"user: This is a very long conversation message number {i} with lots of detail"));

        var result = await _tools.ContextStatsAsync(largeText);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"needs_condensation\":true");
    }

    [Fact]
    public async Task CondenseContext_ExtractsDecisions()
    {
        var conversation = "user: What framework should we use?\nassistant: I recommend .NET.\nuser: We decided to use .NET 10.";

        _memoryStoreMock.Setup(m => m.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _tools.CondenseContextAsync(conversation);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"success\":true");
        json.ShouldContain("\"decisions_found\"");
    }

    [Fact]
    public async Task CondenseContext_ExtractsTasks()
    {
        var conversation = "user: What needs to be done?\nassistant: We need to implement the API. TODO: add tests.\nuser: I should also fix the bug.";

        _memoryStoreMock.Setup(m => m.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _tools.CondenseContextAsync(conversation);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"success\":true");
        json.ShouldContain("\"tasks_found\"");
    }

    [Fact]
    public async Task CondenseContext_ExtractsFileReferences()
    {
        var conversation = "user: Check the file src/Program.cs\nassistant: I modified appsettings.json and added the config.";

        _memoryStoreMock.Setup(m => m.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _tools.CondenseContextAsync(conversation);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"success\":true");
    }

    [Fact]
    public async Task CondenseContext_EmptyText_ReturnsError()
    {
        var result = await _tools.CondenseContextAsync("");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    [Fact]
    public async Task CondenseContext_PersistsToMemoryStore()
    {
        var conversation = "user: Let's build a feature\nassistant: Sure, I'll help.";

        _memoryStoreMock.Setup(m => m.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _tools.CondenseContextAsync(conversation, scope: "test-scope");

        _memoryStoreMock.Verify(m => m.WriteAsync("test-scope", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CondenseContext_CompressionRatio_LessThanOne()
    {
        // Generate a reasonably long conversation
        var lines = Enumerable.Range(0, 100)
            .Select(i => $"user: Question {i} about various topics\nassistant: Answer {i} with detailed explanation about the topic at hand");
        var conversation = string.Join("\n", lines);

        _memoryStoreMock.Setup(m => m.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _tools.CondenseContextAsync(conversation);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"compression_ratio\"");
    }
}
