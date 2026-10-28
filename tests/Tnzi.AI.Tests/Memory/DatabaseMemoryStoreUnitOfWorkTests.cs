using Tnzi.AI.Infrastructure.Memory;
using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// 记忆覆盖写（删旧条目 + 写新条目）必须整体落在调用方的工作单元里。
/// </summary>
/// <remarks>
/// <para>
/// 工作单元的物理事务延迟到首次保存才开启，而覆盖写先用 <c>ExecuteDeleteAsync</c> 删旧条目：那是绕过变更跟踪器的裸 SQL，
/// 不先把事务强开，删除就以自动提交落库。外层工作单元（全局工作单元下的一次请求）随后失败回滚时，
/// 新条目被撤销，旧条目却已经没了：这个范围的记忆被清空，没有任何报错。
/// </para>
/// <para>
/// 必须跑在真实 EF + UnitOfWorkManager 上：Mock 仓储既不会延迟保存，也不执行 <c>ExecuteDelete</c>。
/// 新建条目的 flush 会顺手开启事务，所以种子行要在工作单元之外先落库，删除才是事务里的第一条写入。
/// </para>
/// </remarks>
public class DatabaseMemoryStoreUnitOfWorkTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 让 UnitOfWorkManager 能发现测试 DbContext
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(AiIntegrationDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IRepository<MemoryEntry, Guid>,
            EFCoreRepository<AiIntegrationDbContext, MemoryEntry, Guid>>();
        services.AddScoped(sp => new DatabaseMemoryStore(
            sp.GetRequiredService<IRepository<MemoryEntry, Guid>>(),
            Mock.Of<ILogger<DatabaseMemoryStore>>(),
            sp.GetRequiredService<IUnitOfWorkManager>()));
    }

    private DatabaseMemoryStore Store => ServiceProvider.GetRequiredService<DatabaseMemoryStore>();

    private async Task SeedAsync(MemoryEntry entry)
    {
        DbContext.Set<MemoryEntry>().Add(entry);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<List<string>> ReadContentsAsync(string scope)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<MemoryEntry>().AsNoTracking()
            .Where(e => e.Scope == scope)
            .Select(e => e.Content)
            .ToListAsync();
    }

    [Fact]
    public async Task WriteAsync_InsideAnOuterUnitOfWorkThatRollsBack_KeepsThePreviousEntries()
    {
        await SeedAsync(new MemoryEntry { Scope = "profile", Content = "prefers email" });

        Manager.EnableTransaction();
        await Store.WriteAsync("profile", "prefers phone");
        await Manager.RollbackTransactionAsync();

        (await ReadContentsAsync("profile")).ShouldBe(["prefers email"]);
    }

    [Fact]
    public async Task WriteAsync_ScopeOverload_InsideAnOuterUnitOfWorkThatRollsBack_KeepsThePreviousEntries()
    {
        var scope = MemoryScope.ForAgent(Guid.NewGuid());
        var scopeKey = scope.ToScopeKey();
        await SeedAsync(new MemoryEntry { Scope = scopeKey, Content = "answers briefly", AgentId = scope.AgentId });

        Manager.EnableTransaction();
        await Store.WriteAsync(scope, "answers in detail");
        await Manager.RollbackTransactionAsync();

        (await ReadContentsAsync(scopeKey)).ShouldBe(["answers briefly"]);
    }

    [Fact]
    public async Task ClearAsync_InsideAnOuterUnitOfWorkThatRollsBack_KeepsTheEntries()
    {
        await SeedAsync(new MemoryEntry { Scope = "profile", Content = "prefers email" });

        Manager.EnableTransaction();
        await Store.ClearAsync("profile");
        await Manager.RollbackTransactionAsync();

        (await ReadContentsAsync("profile")).ShouldBe(["prefers email"]);
    }

    [Fact]
    public async Task AppendAsync_TrimInsideAnOuterUnitOfWorkThatRollsBack_KeepsTheTrimmedEntries()
    {
        await SeedAsync(new MemoryEntry { Scope = "notes", Content = "first" });
        await SeedAsync(new MemoryEntry { Scope = "notes", Content = "second" });

        var options = new AIOptions();
        options.ContextProviders.Memory.MaxEntriesPerScope = 1;
        var store = new DatabaseMemoryStore(
            ServiceProvider.GetRequiredService<IRepository<MemoryEntry, Guid>>(),
            Mock.Of<ILogger<DatabaseMemoryStore>>(),
            Manager,
            aiOptions: new StaticOptionsMonitor<AIOptions>(options));

        Manager.EnableTransaction();
        await store.AppendAsync("notes", "third");
        await Manager.RollbackTransactionAsync();

        (await ReadContentsAsync("notes")).ShouldBe(["first", "second"], ignoreOrder: true);
    }

    [Fact]
    public async Task WriteAsync_InsideAnOuterUnitOfWorkThatCommits_ReplacesTheEntries()
    {
        await SeedAsync(new MemoryEntry { Scope = "profile", Content = "prefers email" });

        Manager.EnableTransaction();
        await Store.WriteAsync("profile", "prefers phone");
        await Manager.CommitTransactionAsync();

        (await ReadContentsAsync("profile")).ShouldBe(["prefers phone"]);
    }
}
