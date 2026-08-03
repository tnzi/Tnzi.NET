using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Tools;

public class RequiresSkillToolMiddlewareTests
{
    [Fact]
    public async Task AgentExecutor_ExecuteStreamingAsync_MissingRequiredSkill_MarksToolCallAsFailed()
    {
        var toolInvoked = false;
        var tool = AIFunctionFactory.Create(() =>
        {
            toolInvoked = true;
            return "deployed";
        }, "deploy", "Deployment tool");

        var registry = new Mock<IToolRegistry>();
        registry.Setup(x => x.GetAllTools()).Returns(
        [
            new ToolDefinition
            {
                Name = "deploy",
                RequiresSkillSlugs = ["safe-deploy"]
            }
        ]);

        var skillLoadTracker = new Mock<ISkillLoadTracker>();
        skillLoadTracker.Setup(x => x.IsLoaded("safe-deploy")).Returns(false);

        var middleware = new RequiresSkillToolMiddleware(
            registry.Object,
            skillLoadTracker.Object,
            NullLogger<RequiresSkillToolMiddleware>.Instance);

        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateToolCallStream(ct);
                }

                var toolResult = messages.Last().Contents.OfType<FunctionResultContent>().Single();
                toolResult.Result!.ToString()!.ShouldContain("Required skills");
                return CreateTextStream(["guidelines loaded"], ct);
            });

        var executor = new AgentExecutor(client.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [tool],
            Middlewares = [middleware]
        });

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "deploy now")], CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        toolInvoked.ShouldBeFalse();
        skillLoadTracker.Verify(x => x.MarkLoaded("safe-deploy"), Times.Once);

        var toolCall = chunks.Single(x => x.ToolCalls is { Count: > 0 }).ToolCalls!.Single();
        toolCall.Name.ShouldBe("deploy");
        toolCall.IsSuccess.ShouldBeFalse();
        toolCall.Error!.ShouldContain("Required skills not loaded");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateToolCallStream([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("call_1", "deploy")]
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateTextStream(
        IEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(chunk)]
            };
        }

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            FinishReason = ChatFinishReason.Stop,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 4, OutputTokenCount = 2 })]
        };
    }
}
