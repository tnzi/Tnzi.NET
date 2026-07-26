using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// B15: 多租户原生 SQL 隔离 - 验证 TenantId 谓词仅在存在非空租户上下文时追加，
/// 单租户 / host（tenantId 为 null）时不追加（保持现有行为，避免 column "TenantId" does not exist）。
/// </summary>
public class RagTenantSqlFilterTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    #region RagTenantSqlFilter.BuildPredicate

    [Fact]
    public void BuildPredicate_NullTenant_ReturnsEmpty()
    {
        RagTenantSqlFilter.BuildPredicate(null).ShouldBe(string.Empty);
        RagTenantSqlFilter.BuildPredicate(null, columnQualifier: "c.").ShouldBe(string.Empty);
    }

    [Fact]
    public void BuildPredicate_NonNullTenant_AppendsPredicate()
    {
        RagTenantSqlFilter.BuildPredicate(TenantId)
            .ShouldBe(" AND \"TenantId\" = @tenantId");
    }

    [Fact]
    public void BuildPredicate_NonNullTenant_WithQualifier_AppendsQualifiedPredicate()
    {
        RagTenantSqlFilter.BuildPredicate(TenantId, columnQualifier: "c.")
            .ShouldBe(" AND c.\"TenantId\" = @tenantId");
    }

    [Fact]
    public void AddParameter_NullTenant_AddsNoParameter()
    {
        using var cmd = new NpgsqlCommand();

        RagTenantSqlFilter.AddParameter(cmd, null);

        cmd.Parameters.Count.ShouldBe(0);
    }

    [Fact]
    public void AddParameter_NonNullTenant_AddsTenantParameter()
    {
        using var cmd = new NpgsqlCommand();

        RagTenantSqlFilter.AddParameter(cmd, TenantId);

        cmd.Parameters.Count.ShouldBe(1);
        cmd.Parameters[0].ParameterName.ShouldBe("tenantId");
        cmd.Parameters[0].Value.ShouldBe(TenantId);
    }

    #endregion

    #region PgVectorStore.BuildSearchSql

    [Fact]
    public void VectorSql_NullTenant_HasNoTenantPredicate()
    {
        var kbScoped = PgVectorStore.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: true, extraWhere: "", tenantId: null);
        var searchAll = PgVectorStore.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: false, extraWhere: "", tenantId: null);

        kbScoped.ShouldNotContain("TenantId");
        searchAll.ShouldNotContain("TenantId");
    }

    [Fact]
    public void VectorSql_NonNullTenant_KbScoped_HasUnqualifiedTenantPredicate()
    {
        var sql = PgVectorStore.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: true, extraWhere: "", tenantId: TenantId);

        sql.ShouldContain("AND \"TenantId\" = @tenantId");
    }

    [Fact]
    public void VectorSql_NonNullTenant_SearchAll_HasQualifiedTenantPredicate()
    {
        var sql = PgVectorStore.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: false, extraWhere: "", tenantId: TenantId);

        sql.ShouldContain("AND c.\"TenantId\" = @tenantId");
    }

    #endregion

    #region PgFullTextSearchProvider.BuildSearchSql

    [Fact]
    public void KeywordSql_NullTenant_HasNoTenantPredicate()
    {
        var kbScoped = PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: true, tenantId: null);
        var searchAll = PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: false, tenantId: null);

        kbScoped.ShouldNotContain("TenantId");
        searchAll.ShouldNotContain("TenantId");
    }

    [Fact]
    public void KeywordSql_NonNullTenant_KbScoped_HasUnqualifiedTenantPredicate()
    {
        var sql = PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: true, tenantId: TenantId);

        sql.ShouldContain("AND \"TenantId\" = @tenantId");
    }

    [Fact]
    public void KeywordSql_NonNullTenant_SearchAll_HasQualifiedTenantPredicate()
    {
        var sql = PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: false, tenantId: TenantId);

        sql.ShouldContain("AND c.\"TenantId\" = @tenantId");
    }

    #endregion

    #region EnsureTenantScopeForSearchAll (MT host-context search-all fail-closed)

    [Fact]
    public void EnsureTenantScope_MtEnabled_NullTenant_NoKbScope_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            RagTenantSqlFilter.EnsureTenantScopeForSearchAll(
                multiTenancyEnabled: true, tenantId: null, hasKnowledgeBaseScope: false));

        ex.Message.ShouldContain("tenant context");
        ex.Message.ShouldContain("multi-tenancy");
    }

    [Fact]
    public void EnsureTenantScope_MtEnabled_NullTenant_WithKbScope_DoesNotThrow()
    {
        Should.NotThrow(() =>
            RagTenantSqlFilter.EnsureTenantScopeForSearchAll(
                multiTenancyEnabled: true, tenantId: null, hasKnowledgeBaseScope: true));
    }

    [Fact]
    public void EnsureTenantScope_MtEnabled_WithTenant_NoKbScope_DoesNotThrow()
    {
        Should.NotThrow(() =>
            RagTenantSqlFilter.EnsureTenantScopeForSearchAll(
                multiTenancyEnabled: true, tenantId: TenantId, hasKnowledgeBaseScope: false));
    }

    [Fact]
    public void EnsureTenantScope_MtDisabled_NullTenant_NoKbScope_DoesNotThrow()
    {
        // 单租户部署：维持现状，search-all 不受影响
        Should.NotThrow(() =>
            RagTenantSqlFilter.EnsureTenantScopeForSearchAll(
                multiTenancyEnabled: false, tenantId: null, hasKnowledgeBaseScope: false));
    }

    [Fact]
    public async Task PgVectorStore_MtEnabled_HostContext_SearchAll_FailsClosed()
    {
        // host 上下文（ICurrentTenant 解析为 null）+ MT 启用 + 无 KB 范围 → 守卫先于连接打开抛出
        var store = new PgVectorStore(
            BuildConfiguration(),
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions()),
            NullLogger<PgVectorStore>.Instance,
            BuildServiceProvider(tenantId: null),
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = true }));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SearchAsync([0.1f], topK: 5, knowledgeBaseId: null));

        ex.Message.ShouldContain("tenant context");
    }

    [Fact]
    public async Task PgFullTextSearchProvider_MtEnabled_HostContext_SearchAll_FailsClosed()
    {
        var provider = new PgFullTextSearchProvider(
            BuildConfiguration(),
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions()),
            NullLogger<PgFullTextSearchProvider>.Instance,
            currentTenant: null,
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = true }));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.SearchAsync("query", topK: 5, knowledgeBaseId: null));

        ex.Message.ShouldContain("tenant context");
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=test"
        })
        .Build();

    private static IServiceProvider BuildServiceProvider(Guid? tenantId)
    {
        var services = new ServiceCollection();
        var tenantMock = new Mock<Tnzi.MultiTenancy.ICurrentTenant>();
        tenantMock.SetupGet(t => t.Id).Returns(tenantId);
        services.AddScoped(_ => tenantMock.Object);
        return services.BuildServiceProvider();
    }

    #endregion

    #region Captive-dependency regression (PgVectorStore is Singleton, ICurrentTenant is Scoped)

    [Fact]
    public void PgVectorStore_Singleton_DoesNotCaptureScopedCurrentTenant()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=test"
            })
            .Build());
        services.AddOptions<AIRagOptions>();
        services.AddScoped<Tnzi.MultiTenancy.ICurrentTenant, Tnzi.MultiTenancy.CurrentTenant>();

        // 与生产一致：PgVectorStore 注册为 Singleton
        services.AddSingleton<PgVectorStore>();

        // ValidateScopes + ValidateOnBuild 会在 Singleton 直接依赖 Scoped 时抛出 —— 锁定本修复
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        provider.GetRequiredService<PgVectorStore>().ShouldNotBeNull();
    }

    #endregion
}
