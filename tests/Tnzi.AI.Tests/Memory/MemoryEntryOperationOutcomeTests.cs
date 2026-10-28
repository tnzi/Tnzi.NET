using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// update_memory / delete_memory 只在记忆真的被改了时回报成功。
/// </summary>
/// <remarks>
/// <para>
/// ★ 两处静默成功：<see cref="IMemoryStore"/> 按条目更新 / 删除的默认实现是空操作（只实现了抽象成员的自定义存储每一次都是），
/// <see cref="DatabaseMemoryStore"/> 对不存在或不属于本地 scope 的条目只记一条 Warning 就返回。而 search_memory 同时返回
/// 共享 scope 与项目快照 scope 的条目，工具却只对本地 scope 改动。用户说「忘掉这一条」，模型拿着搜到的 id 调 delete_memory，
/// 工具回报 deleted，条目原样还在。
/// </para>
/// </remarks>
public class MemoryEntryOperationOutcomeTests
{
    /// <summary>默认配置开着用户隔离：没有用户的调用被拒绝，所以工具以一个登录用户运行。</summary>
    private static readonly Guid UserId = Guid.Parse("6d1f6c2e-3b7a-4f0e-9c55-2a8b1e0d4f11");

    [Fact]
    public async Task StoreWithoutEntryOperations_FailsInsteadOfPretendingToSucceed()
    {
        IMemoryStore store = new MinimalMemoryStore();

        await Should.ThrowAsync<NotSupportedException>(() => store.DeleteEntryAsync("default", Guid.NewGuid()));
        await Should.ThrowAsync<NotSupportedException>(() => store.UpdateEntryAsync("default", Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task DeleteMemory_OnStoreWithoutEntryOperations_ReportsAnError()
    {
        var tools = CreateTools(new MinimalMemoryStore());

        var result = await tools.DeleteMemoryAsync(Guid.NewGuid().ToString());

        StatusOf(result).ShouldBe("error");
    }

    [Fact]
    public async Task DeleteMemory_EntryFromASharedScope_ReportsNotFoundAndKeepsTheEntry()
    {
        var (store, repository, entry) = CreateDatabaseStoreWithSharedEntry();
        var tools = CreateTools(store);

        var result = await tools.DeleteMemoryAsync(entry.Id.ToString());

        StatusOf(result).ShouldBe("not_found");
        repository.Verify(r => r.DeleteAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateMemory_EntryFromASharedScope_ReportsNotFoundAndKeepsTheEntry()
    {
        var (store, repository, entry) = CreateDatabaseStoreWithSharedEntry();
        var tools = CreateTools(store);

        var result = await tools.UpdateMemoryAsync(entry.Id.ToString(), "rewritten");

        StatusOf(result).ShouldBe("not_found");
        repository.Verify(r => r.UpdateAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMemory_EntryInTheLocalScope_IsDeleted()
    {
        var entry = new MemoryEntry { Id = Guid.NewGuid(), Scope = LocalScopeKey(), Content = "team prefers tabs", CreationTime = DateTime.UtcNow };
        var repository = new Mock<IRepository<MemoryEntry, Guid>>();
        repository.Setup(r => r.GetAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        var tools = CreateTools(new DatabaseMemoryStore(repository.Object, NullLogger<DatabaseMemoryStore>.Instance));

        var result = await tools.DeleteMemoryAsync(entry.Id.ToString());

        StatusOf(result).ShouldBe("deleted");
        repository.Verify(r => r.DeleteAsync(entry, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (DatabaseMemoryStore Store, Mock<IRepository<MemoryEntry, Guid>> Repository, MemoryEntry Entry) CreateDatabaseStoreWithSharedEntry()
    {
        var entry = new MemoryEntry { Id = Guid.NewGuid(), Scope = "shared", Content = "team prefers tabs", CreationTime = DateTime.UtcNow };
        var repository = new Mock<IRepository<MemoryEntry, Guid>>();
        repository.Setup(r => r.GetAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        return (new DatabaseMemoryStore(repository.Object, NullLogger<DatabaseMemoryStore>.Instance), repository, entry);
    }

    /// <summary>工具在默认配置、以 <see cref="UserId"/> 登录且无 Agent 时改动的 scope。</summary>
    private static string LocalScopeKey()
    {
        var options = new MemoryOptions();
        return MemoryScopeResolver.BuildLocalScope(options.DefaultScope, options.EnableUserIsolation, options.EnableAgentIsolation, UserId).ToScopeKey();
    }

    private static MemoryTools CreateTools(IMemoryStore store)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(UserId);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return new(store, NullLogger<MemoryTools>.Instance, new StaticOptionsMonitor<AIOptions>(new AIOptions()), user.Object);
    }

    private static string? StatusOf(object result)
        => JsonSerializer.SerializeToElement(result).GetProperty("status").GetString();

    /// <summary>只实现抽象成员的自定义存储（文档里替换存储的写法）。</summary>
    private sealed class MinimalMemoryStore : IMemoryStore
    {
        public Task<string?> ReadAsync(string scope, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task WriteAsync(string scope, string content, CancellationToken ct = default) => Task.CompletedTask;

        public Task AppendAsync(string scope, string entry, CancellationToken ct = default) => Task.CompletedTask;

        public Task ClearAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
    }
}
