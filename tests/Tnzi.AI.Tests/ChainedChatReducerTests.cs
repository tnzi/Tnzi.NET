namespace Tnzi.AI.Tests;

/// <summary>
/// ChainedChatReducer 单元测试 - 验证链式历史压缩器行为
/// </summary>
public class ChainedChatReducerTests
{
    [Fact]
    public async Task Reduce_ChainsReducersInOrder()
    {
        // 第一个 reducer：20 条消息 → 10 条
        var firstMessages = Enumerable.Range(0, 10).Select(i => new ChatMessage(ChatRole.User, $"After First {i}")).ToList();
        var firstResult = new HistoryReductionResult(firstMessages, 20, 10, 8000, 4000, "First");

        var mockFirst = new Mock<IHistoryReducer>();
        mockFirst
            .Setup(r => r.ReduceAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstResult);

        // 第二个 reducer：10 条消息 → 5 条
        var secondMessages = Enumerable.Range(0, 5).Select(i => new ChatMessage(ChatRole.User, $"After Second {i}")).ToList();
        var secondResult = new HistoryReductionResult(secondMessages, 10, 5, 4000, 2000, "Second");

        var mockSecond = new Mock<IHistoryReducer>();
        mockSecond
            .Setup(r => r.ReduceAsync(firstMessages, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondResult);

        var reducer = new ChainedChatReducer([mockFirst.Object, mockSecond.Object]);
        var input = Enumerable.Range(0, 20).Select(i => new ChatMessage(ChatRole.User, $"Message {i}")).ToList();

        var result = await reducer.ReduceAsync(input);

        // 输出应来自第二个 reducer
        result.Messages.ShouldBeSameAs(secondMessages);
        // StrategyName 应组合两个
        result.StrategyName.ShouldBe("First+Second");
        // OriginalMessageCount 来自第一个 reducer
        result.OriginalMessageCount.ShouldBe(20);
        // ReducedMessageCount 来自最后一个 reducer
        result.ReducedMessageCount.ShouldBe(5);
        // EstimatedOriginalTokens 来自第一个 reducer
        result.EstimatedOriginalTokens.ShouldBe(8000);
        // EstimatedReducedTokens 来自最后一个 reducer
        result.EstimatedReducedTokens.ShouldBe(2000);
    }

    [Fact]
    public async Task Reduce_SingleReducer_DelegatesDirectly()
    {
        var messages = Enumerable.Range(0, 5).Select(i => new ChatMessage(ChatRole.User, $"Message {i}")).ToList();
        var expectedResult = new HistoryReductionResult(messages, 10, 5, 3000, 1500, "MockStrategy");

        var mockReducer = new Mock<IHistoryReducer>();
        mockReducer
            .Setup(r => r.ReduceAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var reducer = new ChainedChatReducer([mockReducer.Object]);
        var input = Enumerable.Range(0, 10).Select(i => new ChatMessage(ChatRole.User, $"Input {i}")).ToList();

        var result = await reducer.ReduceAsync(input);

        // 结果应与 mock 返回一致
        result.ShouldBeSameAs(expectedResult);
        result.StrategyName.ShouldBe("MockStrategy");
        mockReducer.Verify(r => r.ReduceAsync(input, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_EmptyReducerList_Throws()
    {
        // 空集合应抛出 ArgumentException
        Should.Throw<Exception>(() => new ChainedChatReducer(Array.Empty<IHistoryReducer>()));
    }
}
