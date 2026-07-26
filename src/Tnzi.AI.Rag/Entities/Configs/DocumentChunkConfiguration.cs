using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tnzi.AI.Rag.Entities.Configs;

/// <summary>
/// DocumentChunk 实体配置 - 包含 pgvector 列类型映射
/// </summary>
public class DocumentChunkConfiguration : EntityTypeConfigurationBase<DocumentChunk, Guid>
{
    public override Type? DbContextType => typeof(RagDbContext);

    public override void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 从 DI 获取嵌入维度配置，默认 1536（OpenAI text-embedding-ada-002）
        var dimensions = 1536;
        try
        {
            var serviceProvider = (GetDbContext() as IInfrastructure<IServiceProvider>)?.Instance;
            var options = serviceProvider?.GetService<IOptions<AIRagOptions>>();
            if (options?.Value.DefaultEmbeddingDimensions > 0)
            {
                dimensions = options.Value.DefaultEmbeddingDimensions;
            }
        }
        catch { /* 设计时迁移等场景下 DI 不可用，使用默认值 */ }

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.Embedding)
            .HasColumnType($"vector({dimensions})")
            .IsRequired();

        builder.Property(e => e.Metadata)
            .HasMaxLength(4000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.KnowledgeBaseId);
        builder.HasIndex(e => e.DocumentId);
        builder.HasIndex(e => e.ParentChunkId);

        // 自引用外键：父块关系
        builder.HasOne<DocumentChunk>()
            .WithMany()
            .HasForeignKey(e => e.ParentChunkId)
            .OnDelete(DeleteBehavior.SetNull);

        // pgvector IVFFlat 索引在数据库迁移中手动创建，因 EF Core 不支持 IVFFlat 索引类型
    }
}
