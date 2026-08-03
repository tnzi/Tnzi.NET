
namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// InMemoryVectorStore 多租户隔离测试 - 验证 upsert 时租户标记、搜索时租户过滤、
/// 以及 MT 启用 + host 上下文（无租户）时跨库 search-all 的 fail-closed 行为。
/// 语义与 PgVectorStore raw-SQL 路径（RagTenantSqlFilter）对齐。
/// </summary>
public class InMemoryVectorStoreTenantTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly StubCurrentTenant _currentTenant = new();

    /// <summary>
    /// 可变租户桩 - 注册为单例，使 store 在新 scope 中解析到同一实例，
    /// 测试间通过 Id 属性切换当前租户。
    /// </summary>
    private sealed class StubCurrentTenant : Tnzi.MultiTenancy.ICurrentTenant
    {
        public Guid? Id { get; set; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => throw new NotSupportedException();
    }

    private InMemoryVectorStore CreateStore(bool multiTenancyEnabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Tnzi.MultiTenancy.ICurrentTenant>(_currentTenant);
        var serviceProvider = services.BuildServiceProvider();

        return new InMemoryVectorStore(
            NullLogger<InMemoryVectorStore>.Instance,
            serviceProvider,
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = multiTenancyEnabled }));
    }

    private static Task UpsertAsync(InMemoryVectorStore store, Guid kbId, string content)
        => store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f], Guid.NewGuid(), kbId, content, 0);

    [Fact]
    public async Task Search_WithTenantContext_ReturnsOnlyCurrentTenantEntries()
    {
        var store = CreateStore(multiTenancyEnabled: true);
        var kbId = Guid.NewGuid();

        _currentTenant.Id = TenantA;
        await UpsertAsync(store, kbId, "tenant-a chunk");

        _currentTenant.Id = TenantB;
        await UpsertAsync(store, kbId, "tenant-b chunk");

        _currentTenant.Id = TenantA;
        var results = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: null);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("tenant-a chunk");
    }

    [Fact]
    public async Task Search_KbScoped_WithTenantContext_FiltersByTenantAndKb()
    {
        var store = CreateStore(multiTenancyEnabled: true);
        var kbId = Guid.NewGuid();
        var otherKbId = Guid.NewGuid();

        _currentTenant.Id = TenantA;
        await UpsertAsync(store, kbId, "tenant-a kb chunk");
        await UpsertAsync(store, otherKbId, "tenant-a other-kb chunk");

        _currentTenant.Id = TenantB;
        await UpsertAsync(store, kbId, "tenant-b kb chunk");

        _currentTenant.Id = TenantA;
        var results = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: kbId);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("tenant-a kb chunk");
    }

    [Fact]
    public async Task Search_MtEnabled_HostContext_SearchAll_FailsClosed()
    {
        var store = CreateStore(multiTenancyEnabled: true);

        _currentTenant.Id = TenantA;
        await UpsertAsync(store, Guid.NewGuid(), "tenant-a chunk");

        // host 上下文（无租户）发起无 KB 范围的跨库搜索 → 必须拒绝而非放行全部租户数据
        _currentTenant.Id = null;
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: null));

        ex.Message.ShouldContain("tenant context");
    }

    [Fact]
    public async Task Search_MtEnabled_HostContext_KbScoped_DoesNotThrow()
    {
        var store = CreateStore(multiTenancyEnabled: true);
        var kbId = Guid.NewGuid();

        _currentTenant.Id = TenantA;
        await UpsertAsync(store, kbId, "tenant-a chunk");

        // 带明确 KB 范围的查询不受 fail-closed 守卫影响
        _currentTenant.Id = null;
        var results = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: kbId);

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Search_MtDisabled_NoTenant_SearchAll_ReturnsAll()
    {
        // 多租户关闭：行为与修复前一致（不过滤、不拒绝）
        var store = CreateStore(multiTenancyEnabled: false);

        _currentTenant.Id = TenantA;
        await UpsertAsync(store, Guid.NewGuid(), "chunk-1");

        _currentTenant.Id = null;
        await UpsertAsync(store, Guid.NewGuid(), "chunk-2");

        var results = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: null);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LegacyConstructor_NoServiceProvider_BehavesAsSingleTenant()
    {
        // 直接 new（无 IServiceProvider）的测试场景：无租户解析、无过滤、无守卫
        var store = new InMemoryVectorStore(NullLogger<InMemoryVectorStore>.Instance);
        var kbId = Guid.NewGuid();

        await UpsertAsync(store, kbId, "chunk");

        var all = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: null);
        var scoped = await store.SearchAsync([1.0f, 0.0f], topK: 10, knowledgeBaseId: kbId);

        all.Count.ShouldBe(1);
        scoped.Count.ShouldBe(1);
    }
}
