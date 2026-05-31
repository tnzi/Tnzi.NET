namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// MemoryContextProvider 检索式注入测试
/// </summary>
public class MemoryContextProviderRetrievalTests
{
    #region CountMemoryEntries

    [Fact]
    public void CountMemoryEntries_MultipleEntries_ReturnsCorrectCount()
    {
        var content = """
            [preference] User likes dark mode
            [fact] Project uses .NET 10.0
            [decision] Database is PostgreSQL
            """;
        MemoryContextProvider.CountMemoryEntries(content).ShouldBe(3);
    }

    [Fact]
    public void CountMemoryEntries_EmptyString_ReturnsZero()
    {
        MemoryContextProvider.CountMemoryEntries("").ShouldBe(0);
        MemoryContextProvider.CountMemoryEntries("   ").ShouldBe(0);
    }

    [Fact]
    public void CountMemoryEntries_SingleEntry_ReturnsOne()
    {
        MemoryContextProvider.CountMemoryEntries("single memory entry").ShouldBe(1);
    }

    [Fact]
    public void CountMemoryEntries_WithBlankLines_SkipsBlanks()
    {
        var content = "entry1\n\nentry2\n\n\nentry3";
        MemoryContextProvider.CountMemoryEntries(content).ShouldBe(3);
    }

    #endregion

    #region BuildMemoryIndex

    [Fact]
    public void BuildMemoryIndex_ShortEntries_PreservesFullText()
    {
        var content = "[fact] DB is PostgreSQL\n[pref] Dark mode";
        var index = MemoryContextProvider.BuildMemoryIndex(content);

        index.ShouldContain("1. [fact] DB is PostgreSQL");
        index.ShouldContain("2. [pref] Dark mode");
    }

    [Fact]
    public void BuildMemoryIndex_LongEntry_TruncatesAt80()
    {
        var longLine = new string('a', 100);
        var index = MemoryContextProvider.BuildMemoryIndex(longLine);

        index.ShouldContain("...");
        // 80 chars + "..." = truncated
        index.ShouldContain("1. " + new string('a', 80) + "...");
    }

    #endregion

    #region 两阶段注入集成

    [Fact]
    public async Task GetContextAsync_SmallMemory_InjectsFullContent()
    {
        // 少量记忆 (< 20 条) → 全量注入
        var memoryContent = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"[fact] Memory entry {i}"));
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryContent);

        var options = new MemoryOptions { RetrievalModeThreshold = 20 };
        var provider = new MemoryContextProvider(
            store.Object, new MemoryScope("test"),
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: options);

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages[0].Text!;
        text.ShouldContain("Memory entry 1");
        text.ShouldContain("Memory entry 5");
        text.ShouldNotContain("[Memory index");
    }

    [Fact]
    public async Task GetContextAsync_LargeMemory_SwitchesToRetrievalMode()
    {
        // 大量记忆 (> 20 条) → 检索式注入
        var memoryContent = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"[fact] Memory entry {i} about topic {i}"));
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryContent);
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Content = "[fact] Memory entry 5 about topic 5", Score = 0.95 },
                new() { Content = "[fact] Memory entry 12 about topic 12", Score = 0.8 }
            });

        var options = new MemoryOptions { RetrievalModeThreshold = 20, RetrievalTopK = 5 };
        var provider = new MemoryContextProvider(
            store.Object, new MemoryScope("test"),
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: options);

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "tell me about topic 5")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages[0].Text!;
        // 应包含索引
        text.ShouldContain("[Memory index (30 entries)");
        // 应包含 SearchAsync 返回的相关记忆全文
        text.ShouldContain("Memory entry 5 about topic 5");
        text.ShouldContain("Memory entry 12 about topic 12");
    }

    [Fact]
    public async Task GetContextAsync_LargeMemory_SearchesProjectSnapshotAndSharedScopes()
    {
        var projectRoot = @"D:\Repo\Tnzi.NET";
        var options = new MemoryOptions
        {
            EnableProjectSnapshot = true,
            ProjectSnapshotScopePrefix = "project",
            SharedScope = "shared",
            RetrievalModeThreshold = 2,
            RetrievalTopK = 5
        };
        var projectScope = MemoryScopeResolver.ResolveProjectSnapshotScope(
            options.EnableProjectSnapshot,
            options.ProjectSnapshotScopePrefix,
            projectRoot);
        projectScope.ShouldNotBeNull();

        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[local] entry 1\n[local] entry 2\n[local] entry 3");
        store.Setup(s => s.ReadAsync(projectScope!, It.IsAny<CancellationToken>()))
            .ReturnsAsync("[project] topic 5");
        store.Setup(s => s.ReadAsync("shared", It.IsAny<CancellationToken>()))
            .ReturnsAsync("[shared] topic 7");
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[]);
        store.Setup(s => s.SearchAsync(projectScope!, "topic 5", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Content = "[project] topic 5", Score = 0.95 }
            });
        store.Setup(s => s.SearchAsync("shared", "topic 5", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Content = "[shared] topic 7", Score = 0.5 }
            });

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                Metadata = new Dictionary<string, object>
                {
                    ["working_directory"] = projectRoot
                }
            }
        };

        var provider = new MemoryContextProvider(
            store.Object,
            new MemoryScope("test"),
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: options,
            executionContextAccessor: accessor);

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "topic 5")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages[0].Text!;
        text.ShouldContain("[Project Snapshot] [project] topic 5");
        text.ShouldContain("[Shared Memory] [shared] topic 7");
    }

    [Fact]
    public async Task GetContextAsync_AgentBoundScope_InjectsAgentMemory_Headless()
    {
        // 回归测试：Agent X 的 agent-bound 记忆必须在该 Agent 运行时被注入，
        // 即使没有当前用户（headless）。本地 scope 无记忆（模拟 headless 无用户记忆）。
        var agentId = Guid.NewGuid();
        var localScope = new MemoryScope("default"); // headless: 无 UserId
        var agentBoundScope = MemoryScope.ForAgent(agentId, "default");

        var store = new Mock<IMemoryStore>();
        // 本地 scope 读取无内容
        store.Setup(s => s.ReadAsync(
                It.Is<MemoryScope>(sc => !sc.AgentBound), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        // Agent-bound scope 返回管理员预置记忆（按结构化 AgentId 列检索的代理）
        store.Setup(s => s.ReadAsync(
                It.Is<MemoryScope>(sc => sc.AgentBound && sc.AgentId == agentId), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[instruction] Always greet candidates by first name");

        var provider = new MemoryContextProvider(
            store.Object,
            localScope,
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: new MemoryOptions { RetrievalModeThreshold = 20 },
            agentBoundScope: agentBoundScope);

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages![0].Text!;
        // 核心断言：headless 下 agent-bound 记忆被注入 LLM 上下文
        text.ShouldContain("Always greet candidates by first name");
        // 且确实通过 agent-bound 范围读取（而非本地 scope）
        store.Verify(s => s.ReadAsync(
            It.Is<MemoryScope>(sc => sc.AgentBound && sc.AgentId == agentId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetContextAsync_AgentBoundScope_LabeledAlongsideLocalMemory()
    {
        // 当本地记忆与 agent-bound 记忆同时存在时，应作为两个带标签的段一起注入。
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var localScope = new MemoryScope("default", userId);
        var agentBoundScope = MemoryScope.ForAgent(agentId, "default");

        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(
                It.Is<MemoryScope>(sc => !sc.AgentBound), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[fact] user-specific note");
        store.Setup(s => s.ReadAsync(
                It.Is<MemoryScope>(sc => sc.AgentBound && sc.AgentId == agentId), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[instruction] curated agent rule");

        var provider = new MemoryContextProvider(
            store.Object,
            localScope,
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: new MemoryOptions { RetrievalModeThreshold = 20 },
            agentBoundScope: agentBoundScope);

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages![0].Text!;
        text.ShouldContain("### Local Memory");
        text.ShouldContain("### Agent Memory");
        text.ShouldContain("user-specific note");
        text.ShouldContain("curated agent rule");
    }

    [Fact]
    public async Task GetContextAsync_NoAgentBoundScope_DoesNotInjectAgentMemory()
    {
        // 向后兼容：未提供 agent-bound scope 时，仅注入本地记忆，行为不变。
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[fact] local memory only");

        var provider = new MemoryContextProvider(
            store.Object,
            new MemoryScope("test"),
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: new MemoryOptions { RetrievalModeThreshold = 20 });

        var result = await provider.GetContextAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages![0].Text!;
        text.ShouldContain("local memory only");
        text.ShouldNotContain("Agent Memory");
    }

    [Fact]
    public async Task GetContextAsync_LargeMemory_NoUserMessage_FallsBackToFullInjection()
    {
        var memoryContent = string.Join("\n", Enumerable.Range(1, 25).Select(i => $"[fact] Entry {i}"));
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryContent);

        var options = new MemoryOptions { RetrievalModeThreshold = 20 };
        var provider = new MemoryContextProvider(
            store.Object, new MemoryScope("test"),
            NullLogger<MemoryContextProvider>.Instance,
            memoryOptions: options);

        // 无用户消息 → 回退全量注入
        var result = await provider.GetContextAsync([]);

        result.Messages.ShouldNotBeEmpty();
        var text = result.Messages[0].Text!;
        text.ShouldContain("Entry 1");
        text.ShouldNotContain("[Memory index");
    }

    #endregion
}
