using Tnzi.Data;
using Tnzi.MultiTenancy;

namespace Tnzi.AI.Rag.Tests.TestInfrastructure;

/// <summary>
/// 测试用最小 DbContext - 含 <see cref="KnowledgeBase"/>、<see cref="KnowledgeDocument"/> 与图谱两张表
/// （<see cref="KnowledgeGraphNode"/> / <see cref="KnowledgeGraphEdge"/>）。
/// <para>
/// 刻意不引入 <c>DocumentChunk</c>（其 <c>vector(N)</c> 列无法映射到 SQLite），
/// 因为后台任务的状态回写 / KB 计数器一致性测试只需这几张表 + 真实
/// <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c>（SQLite 支持）即可覆盖；分块经替身仓储 / 向量库断言。
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
    public DbSet<KnowledgeGraphNode> KnowledgeGraphNodes { get; set; } = null!;
    public DbSet<KnowledgeGraphEdge> KnowledgeGraphEdges { get; set; } = null!;

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

        modelBuilder.Entity<KnowledgeGraphNode>(b =>
        {
            b.ToTable("RAG_KnowledgeGraphNode");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).IsRequired().HasMaxLength(500);
            b.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
            b.HasIndex(e => e.DocumentId);
        });

        modelBuilder.Entity<KnowledgeGraphEdge>(b =>
        {
            b.ToTable("RAG_KnowledgeGraphEdge");
            b.HasKey(e => e.Id);
            b.Property(e => e.RelationType).IsRequired().HasMaxLength(200);
            b.HasIndex(e => e.DocumentId);
            b.HasOne<KnowledgeGraphNode>().WithMany().HasForeignKey(e => e.SourceNodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<KnowledgeGraphNode>().WithMany().HasForeignKey(e => e.TargetNodeId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureQueryFilters(modelBuilder);
    }
}
