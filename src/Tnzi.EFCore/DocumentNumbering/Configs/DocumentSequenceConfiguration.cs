namespace Tnzi.EFCore.DocumentNumbering;

/// <summary>
/// 单据序列配置（表名无模块前缀，映射为裸 <c>DocumentSequence</c>）
/// </summary>
public class DocumentSequenceConfiguration : EntityTypeConfigurationBase<DocumentSequence, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(s => s.Scope).HasMaxLength(64).IsRequired();

        // 无软删除实体，唯一索引不需要过滤器。
        // ★ 这条唯一索引是 DocumentNumberService 并发首插的唯一兜底：两个事务同时为新 scope 插入首行，
        //   靠它让后到者撞唯一约束、转去走递增路径。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(s => new { s.TenantId, s.Scope }).IsUnique();

            // 宿主作用域（TenantId 为 NULL，无租户上下文的后台作业与宿主身份取号）另建一条：
            // 唯一索引里 NULL 互不相等（SQL Server 上 EF 还会给上面那条自动加 TenantId IS NOT NULL 过滤），
            // 两条 (NULL, scope) 首行都能插进去，两张单据都拿到 1 号。
            // ★ MySQL 不建：它没有部分索引，Pomelo 生成索引时丢掉过滤条件，落到库里是 Scope 上的全局唯一索引，
            //   第二个租户为同一 scope 首次取号就撞约束、重试耗尽后失败。MySQL 上宿主作用域并发首次取号的缺口仍在。
            if (GetDatabaseProviderOrDefault() != DatabaseProvider.MySql)
            {
                builder.HasIndex(s => s.Scope).IsUnique()
                    .HasFilter(IndexFilter.Null(nameof(DocumentSequence.TenantId)).Build())
                    .HasDatabaseName("IX_DocumentSequence_Host_Scope");
            }
        }
        else
        {
            builder.HasIndex(s => s.Scope).IsUnique();
        }
    }
}
