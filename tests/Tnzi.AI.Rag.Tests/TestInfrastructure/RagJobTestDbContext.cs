using Tnzi.Data;
using Tnzi.MultiTenancy;

namespace Tnzi.AI.Rag.Tests.TestInfrastructure;

/// <summary>
/// 测试用最小 DbContext - 仅含 <see cref="KnowledgeBase"/> 与 <see cref="KnowledgeDocument"/>。
/// <para>
/// 刻意不引入 <c>DocumentChunk</c>（其 <c>vector(N)</c> 列无法映射到 SQLite），
/// 因为后台任务的状态回写 / KB 计数器一致性测试只需这两张表 + 真实
/// <c>ExecuteUpdateAsync</c>（SQLite 支持）即可覆盖。
/// </para>
/// </summary>
public class RagJobTestDbContext : TnziDbContext<RagJobTestDbContext>
{
    public RagJobTestDbContext(
        DbContextOptions<RagJobTestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null)
        : base(options, currentUser, currentTenant, dataFilterManager)
    {
    }

    public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
    public DbSet<KnowledgeDocument> KnowledgeDocuments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<KnowledgeBase>(b =>
        {
            b.ToTable("RAG_KnowledgeBase");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.EmbeddingProvider).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<KnowledgeDocument>(b =>
        {
            b.ToTable("RAG_KnowledgeDocument");
            b.HasKey(e => e.Id);
            b.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            b.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        ConfigureQueryFilters(modelBuilder);
    }
}
