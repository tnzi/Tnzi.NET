namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// 自动合并（AutoConsolidate）不得毁掉记忆：整段重写只在没有增量合并时进行，且只接受像样的合并结果。
/// </summary>
/// <remarks>
/// 旧的整段重写把一个范围里的全部条目交给 LLM，拿回什么就经 <c>WriteAsync</c> 删光全部条目、写回一整段。
/// 默认配置同时开着增量合并（每条记忆带 Importance / Category，按条 ADD / UPDATE / DELETE）与整段重写：
/// 条目一过阈值，几十条带元数据的记忆被替换成一行文本，之后增量合并的 UPDATE / DELETE 命中的就是这一整段，
/// 一次「矛盾」判定删光全部记忆。LLM 回答里带一句开场白、或输出被截断，也会原样成为用户唯一的记忆。
/// </remarks>
public class MemoryConsolidationSafetyTests
{
    [Fact]
    public async Task DefaultOptions_WithIncrementalConsolidation_NeverRewritesTheWholeScope()
    {
        var store = CreateStore(existingLines: 40);
        var consolidator = new Mock<IMemoryConsolidator>();
        var client = CreateClient(
            Response("[category=fact importance=0.9] User is the CTO"),
            Response("- merged everything into one line"));

        // 默认值：AutoConsolidate 与 IncrementalConsolidate 同时为 true
        var options = new MemoryOptions { AutoPersist = true, MinTurnsBetweenExtractions = 0, ConsolidateThreshold = 5 };
        var provider = CreateProvider(store.Object, options, client, consolidator.Object);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyConsolidation_ResultWithPreamble_KeepsTheExistingEntries()
    {
        var store = CreateStore(existingLines: 10);
        var client = CreateClient(
            Response("- Prefers dark mode"),
            Response("Here is the consolidated memory:\n- a\n- b\n- c\n- d\n- e"));

        var provider = CreateProvider(store.Object, LegacyOptions(), client);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyConsolidation_TruncatedResult_KeepsTheExistingEntries()
    {
        var store = CreateStore(existingLines: 10);
        var client = CreateClient(
            Response("- Prefers dark mode"),
            Response("- a\n- b\n- c\n- d\n- e\n- f", ChatFinishReason.Length));

        var provider = CreateProvider(store.Object, LegacyOptions(), client);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyConsolidation_CollapsingAlmostEverything_KeepsTheExistingEntries()
    {
        var store = CreateStore(existingLines: 30);
        var client = CreateClient(
            Response("- Prefers dark mode"),
            Response("- User has preferences\n- User has facts"));

        var provider = CreateProvider(store.Object, LegacyOptions(), client);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyConsolidation_PlausibleResult_ReplacesTheScope()
    {
        var store = CreateStore(existingLines: 10);
        const string consolidated = "- a\n- b\n- c\n- d\n- e\n- f";
        var client = CreateClient(Response("- Prefers dark mode"), Response(consolidated));

        var provider = CreateProvider(store.Object, LegacyOptions(), client);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), consolidated, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// DELETE 表示新记忆与旧记忆矛盾：旧的删掉，新的必须写进去。
    /// 此前只删不写，「搬到了温哥华」把「住在多伦多」删掉之后，用户的住址一条都不剩。
    /// </summary>
    [Fact]
    public async Task IncrementalConsolidation_Delete_RemovesTheContradictedEntryAndStoresTheNewFact()
    {
        var oldEntryId = Guid.NewGuid();
        var store = CreateStore(existingLines: 1);
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[new() { Id = oldEntryId, Content = "User lives in Toronto", Score = 0.9 }]);
        var consolidator = new Mock<IMemoryConsolidator>();
        consolidator.Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Delete, null, oldEntryId));
        var client = CreateClient(
            Response("[category=fact importance=0.8] User moved to Vancouver"),
            Response("unused"));
        var options = new MemoryOptions { AutoPersist = true, AutoConsolidate = false, MinTurnsBetweenExtractions = 0 };
        var scope = new MemoryScope("default", Guid.NewGuid());
        var provider = new MemoryContextProvider(store.Object, scope, Mock.Of<ILogger<MemoryContextProvider>>(), client, options, consolidator.Object);

        await provider.OnCompletedAsync(Conversation());

        store.Verify(s => s.DeleteEntryAsync(scope.ToScopeKey(), oldEntryId, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), "User moved to Vancouver", 0.8, "fact", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>没有增量合并器时走的整段重写路径。</summary>
    private static MemoryOptions LegacyOptions() => new()
    {
        AutoPersist = true,
        AutoConsolidate = true,
        IncrementalConsolidate = false,
        ConsolidateThreshold = 5,
        MinTurnsBetweenExtractions = 0
    };

    private static List<ChatMessage> Conversation() =>
    [
        new(ChatRole.User, "I like dark mode"),
        new(ChatRole.Assistant, "Noted!")
    ];

    private static Mock<IMemoryStore> CreateStore(int existingLines)
    {
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Join("\n", Enumerable.Range(1, existingLines).Select(i => $"- Memory item {i}")));
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[]);
        return store;
    }

    private static ChatResponse Response(string text, ChatFinishReason? finishReason = null)
        => new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finishReason ?? ChatFinishReason.Stop };

    private static IChatClientFactory CreateClient(ChatResponse extraction, ChatResponse consolidation)
    {
        var client = new Mock<IChatClient>();
        client.SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extraction)
            .ReturnsAsync(consolidation);
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        return factory.Object;
    }

    private static MemoryContextProvider CreateProvider(
        IMemoryStore store, MemoryOptions options, IChatClientFactory factory, IMemoryConsolidator? consolidator = null)
        => new(store, new MemoryScope("default", Guid.NewGuid()), Mock.Of<ILogger<MemoryContextProvider>>(), factory, options, consolidator);
}
