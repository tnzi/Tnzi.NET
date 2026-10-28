using Tnzi.AI.Infrastructure.ContextProviders.Contributors;
using Tnzi.AI.Infrastructure.Memory;
using BuiltInMemoryTools = Tnzi.AI.Tools.BuiltIn.MemoryTools;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// 记忆的归属人取自<b>运行本身</b>（请求的 UserId），而不是当前 HTTP 作用域里的登录用户。
/// </summary>
/// <remarks>
/// 两者在代人发起的运行上不同：管理员在运行控制台续跑用户 X 的运行时，<c>AgentRuntime.ResumeAsync</c>
/// 以 X（运行归属人）续跑，而作用域里的 <see cref="ICurrentUser"/> 是管理员。按登录用户取归属人，
/// 管理员自己的实体记忆被注入 X 的运行（模型可能复述进 X 的线程并落库），从 X 的对话里抽出的实体又记在管理员名下。
/// 读与写必须用同一个解析：其余记忆贡献者早已取请求的 UserId。
/// </remarks>
public class MemoryOwnerResolutionTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task EntityMemory_RunOnBehalfOfAnotherUser_ReadsAndWritesTheRunOwnersEntities()
    {
        var store = new Mock<IEntityMemoryStore>();
        store.Setup(s => s.GetRelevantEntitiesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityMemoryEntry>());
        List<EntityMemoryEntry>? upserted = null;
        store.Setup(s => s.UpsertEntitiesAsync(It.IsAny<IEnumerable<EntityMemoryEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EntityMemoryEntry>, CancellationToken>((entries, _) => upserted = entries.ToList())
            .Returns(Task.CompletedTask);

        var provider = CreateEntityMemoryProvider(store.Object, agentId: Guid.NewGuid());

        await provider.GetContextAsync([new ChatMessage(ChatRole.User, "Who is Alice?")]);
        await provider.OnCompletedAsync(
        [
            new ChatMessage(ChatRole.User, "Alice works at Contoso"),
            new ChatMessage(ChatRole.Assistant, "Noted.")
        ]);

        store.Verify(s => s.GetRelevantEntitiesAsync(Owner, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.GetRelevantEntitiesAsync(Admin, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        upserted.ShouldNotBeNull();
        upserted.ShouldAllBe(e => e.UserId == Owner);
    }

    [Fact]
    public async Task MemoryTools_RunOnBehalfOfAnotherUser_SavesIntoTheRunOwnersScope()
    {
        var store = new RecordingMemoryStore();
        var accessor = new AgentExecutionContextAccessor { CurrentRequest = new AgentRunRequest { UserId = Owner } };
        try
        {
            var tools = CreateMemoryTools(store, accessor, CurrentUser(Admin));

            await tools.SaveMemoryAsync("Prefers metric units");

            store.AppendedScopes.ShouldHaveSingleItem();
            store.AppendedScopes[0].ShouldContain(Owner.ToString("N"));
            store.AppendedScopes[0].ShouldNotContain(Admin.ToString("N"));
        }
        finally
        {
            accessor.CurrentRequest = null;
        }
    }

    [Fact]
    public async Task MemoryTools_UserIsolationWithoutAnyUser_RefusesInsteadOfUsingTheSharedScope()
    {
        // 隔离开启却解析不到任何用户（如外部渠道的匿名会话）：以前落进所有无用户运行共用的 default 范围，
        // 一个渠道用户说的「记住我的地址」会被另一个渠道用户检索到
        var store = new RecordingMemoryStore();
        var tools = CreateMemoryTools(store, new AgentExecutionContextAccessor(), currentUser: null);

        var saved = await tools.SaveMemoryAsync("My address is 1 Main St");
        var searched = await tools.SearchMemoryAsync("address");

        store.AppendedScopes.ShouldBeEmpty();
        store.SearchedScopes.ShouldBeEmpty();
        JsonSerializer.Serialize(saved).ShouldContain("\"status\":\"error\"");
        JsonSerializer.Serialize(searched).ShouldContain("\"count\":0");
    }

    [Fact]
    public async Task MemoryTools_NoRunContext_FallsBackToTheSignedInUser()
    {
        var store = new RecordingMemoryStore();
        var tools = CreateMemoryTools(store, new AgentExecutionContextAccessor(), CurrentUser(Admin));

        await tools.SaveMemoryAsync("Prefers dark mode");

        store.AppendedScopes.ShouldHaveSingleItem();
        store.AppendedScopes[0].ShouldContain(Admin.ToString("N"));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static IContextProvider CreateEntityMemoryProvider(IEntityMemoryStore store, Guid agentId)
    {
        var options = new AIOptions();
        options.ContextProviders.EntityMemory.Enabled = true;
        var optionsMonitor = new StaticOptionsMonitor<AIOptions>(options);

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """[{"name":"Alice","type":"person","properties":{}}]""")));
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(chatClient.Object);

        // 作用域里登录的是管理员；运行（请求）属于 Owner
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(new LlmEntityExtractor(factory.Object, optionsMonitor, Mock.Of<ILogger<LlmEntityExtractor>>()))
            .AddSingleton<IOptionsMonitor<AIOptions>>(optionsMonitor)
            .AddSingleton<IAgentExecutionContextAccessor>(new AgentExecutionContextAccessor())
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(CurrentUser(Admin))
            .BuildServiceProvider();

        var contributor = ActivatorUtilities.CreateInstance<EntityMemoryContributor>(services);
        var provider = contributor.TryCreate(new ContextProviderCreationContext { UserId = Owner, AgentId = agentId });
        provider.ShouldNotBeNull();
        return provider!;
    }

    private static BuiltInMemoryTools CreateMemoryTools(IMemoryStore store, IAgentExecutionContextAccessor accessor, ICurrentUser? currentUser)
        => new(store, NullLogger<BuiltInMemoryTools>.Instance, new StaticOptionsMonitor<AIOptions>(new AIOptions()), currentUser, accessor);

    private static ICurrentUser CurrentUser(Guid id)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(id);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user.Object;
    }

    /// <summary>只记录读写落在哪个 scope 键上。</summary>
    private sealed class RecordingMemoryStore : IMemoryStore
    {
        public List<string> AppendedScopes { get; } = [];
        public List<string> SearchedScopes { get; } = [];

        public Task<string?> ReadAsync(string scope, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task WriteAsync(string scope, string content, CancellationToken ct = default) => Task.CompletedTask;

        public Task AppendAsync(string scope, string entry, CancellationToken ct = default)
        {
            AppendedScopes.Add(scope);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string scope, string query, int maxResults = 10, CancellationToken ct = default)
        {
            SearchedScopes.Add(scope);
            return Task.FromResult<IReadOnlyList<MemorySearchResult>>([]);
        }

        public Task ClearAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
    }
}
