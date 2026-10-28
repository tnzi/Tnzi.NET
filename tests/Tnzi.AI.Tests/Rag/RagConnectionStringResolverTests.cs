namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 手写 Npgsql 的检索 / 删除组件连的库，必须就是摄取经 <c>RagDbContext</c> 写入的那个库。
/// </summary>
/// <remarks>
/// ★ 此前优先取 <c>ConnectionStrings:Default</c>，只在它为空时才看 <c>RagDbContext</c> 自己的条目：RAG 按文档单独指向一个库、
/// 而宿主恰好有一条 <c>ConnectionStrings:Default</c>（健康检查、后台任务常用）时，摄取写进 RAG 库，检索与删除却打开主库：
/// 检索被吞成「没有相关信息」，删除文档 / 知识库 500 或删错库、分块永远留在 RAG 库里。
/// </remarks>
public class RagConnectionStringResolverTests
{
    private const string RagEntry = "Host=rag-db;Database=rag;Username=rag;Password=secret";
    private const string DefaultEntry = "Host=main-db;Database=main;Username=app;Password=other";

    [Fact]
    public void RagDbContextEntry_WinsOverConnectionStringsDefault()
    {
        var configuration = Build(new()
        {
            ["ConnectionStrings:Default"] = DefaultEntry,
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:DbContextType"] = "MyApp.Data.AppDbContext, MyApp",
            ["Database:DbContexts:0:ConnectionString"] = DefaultEntry,
            ["Database:DbContexts:1:Name"] = "Rag",
            ["Database:DbContexts:1:DbContextType"] = "Tnzi.AI.Rag.Data.RagDbContext, Tnzi.AI.Rag",
            ["Database:DbContexts:1:ConnectionString"] = RagEntry
        });

        RagConnectionStringResolver.Resolve(configuration, "test").ShouldBe(RagEntry);
    }

    [Fact]
    public void EntryDiscoveredByName_IsTheRagDbContextEntry()
    {
        // 只写 Name 的条目由 EF 模块按「{Name}DbContext」自动发现，写入走的就是它
        var configuration = Build(new()
        {
            ["ConnectionStrings:Default"] = DefaultEntry,
            ["Database:DbContexts:0:Name"] = "Rag",
            ["Database:DbContexts:0:ConnectionString"] = RagEntry
        });

        RagConnectionStringResolver.Resolve(configuration, "test").ShouldBe(RagEntry);
    }

    [Fact]
    public void Placeholders_AreExpanded_TheWayTheDbContextExpandsThem()
    {
        var configuration = Build(new()
        {
            ["RAG_DB_PASSWORD"] = "from-config",
            ["Database:DbContexts:0:Name"] = "Rag",
            ["Database:DbContexts:0:DbContextType"] = "Tnzi.AI.Rag.Data.RagDbContext, Tnzi.AI.Rag",
            ["Database:DbContexts:0:ConnectionString"] = "Host=rag-db;Database=rag;Username=rag;Password=${RAG_DB_PASSWORD}"
        });

        RagConnectionStringResolver.Resolve(configuration, "test")
            .ShouldBe("Host=rag-db;Database=rag;Username=rag;Password=from-config");
    }

    [Fact]
    public void UnresolvedPlaceholder_IsRefused_NamingTheVariable()
    {
        var configuration = Build(new()
        {
            ["Database:DbContexts:0:Name"] = "Rag",
            ["Database:DbContexts:0:ConnectionString"] = "Host=rag-db;Password=${RAG_DB_PASSWORD_THAT_IS_NOT_SET_ANYWHERE}"
        });

        var ex = Should.Throw<InvalidOperationException>(() => RagConnectionStringResolver.Resolve(configuration, "test"));
        ex.Message.ShouldContain("RAG_DB_PASSWORD_THAT_IS_NOT_SET_ANYWHERE");
    }

    [Fact]
    public void WithoutARagEntry_FallsBackToConnectionStringsDefault()
    {
        var configuration = Build(new()
        {
            ["ConnectionStrings:Default"] = DefaultEntry,
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:DbContextType"] = "MyApp.Data.AppDbContext, MyApp",
            ["Database:DbContexts:0:ConnectionString"] = "Host=elsewhere"
        });

        RagConnectionStringResolver.Resolve(configuration, "test").ShouldBe(DefaultEntry);
    }

    [Fact]
    public void RagEntryWithoutAConnectionString_FallsBackToConnectionStringsDefault()
    {
        var configuration = Build(new()
        {
            ["ConnectionStrings:Default"] = DefaultEntry,
            ["Database:DbContexts:0:Name"] = "Rag",
            ["Database:DbContexts:0:DbContextType"] = "Tnzi.AI.Rag.Data.RagDbContext, Tnzi.AI.Rag"
        });

        RagConnectionStringResolver.Resolve(configuration, "test").ShouldBe(DefaultEntry);
    }

    [Fact]
    public void Neither_Throws_WithGuidance()
    {
        var ex = Should.Throw<InvalidOperationException>(() => RagConnectionStringResolver.Resolve(Build(new()), "PgVectorStore"));

        ex.Message.ShouldContain("PgVectorStore");
        ex.Message.ShouldContain("Database:DbContexts");
    }

    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
