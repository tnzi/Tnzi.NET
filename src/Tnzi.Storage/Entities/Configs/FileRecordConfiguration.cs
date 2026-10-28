namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileRecord 实体配置类
/// </summary>
public class FileRecordConfiguration : EntityTypeConfigurationBase<FileRecord, Guid>
{
    public override void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 表名显式指定为 "Record" 以保持与现有数据库架构兼容；否则由 TableNamePrefix 生成 Storage_FileRecord
        builder.ToTable("Record");

        builder.Property(e => e.FileName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OriginalName).HasMaxLength(256);
        builder.Property(e => e.Extension).HasMaxLength(32);
        builder.Property(e => e.ContentType).HasMaxLength(128);
        builder.Property(e => e.Path).HasMaxLength(256);
        builder.Property(e => e.Md5Hash).HasMaxLength(64);
        builder.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("Local");
        builder.Property(e => e.ThumbnailPath).HasMaxLength(256);
        // 列默认 1 是给「没人碰过」的行准备的，可 EF Core 会把 CLR 默认值 0 当成「没设置」而省掉
        // 这一列，于是服务层每一处刻意写的 ReferenceCount = 0（临时上传 / 复制 / 压缩 / 解压 /
        // 分片上传完成）都静默落成 1，孤儿回收对它们整体失效。哨兵改成 -1（业务上不存在的值）
        // 之后 0 是一个真值，会原样写进 INSERT；不碰这个属性的行仍由 CLR 初始化器 = 1 兜住。
        // 哨兵只是模型元数据，不进迁移快照，消费方零迁移。
        builder.Property(e => e.ReferenceCount).HasDefaultValue(1).HasSentinel(-1);
        // ★ 计数只由原子 SQL 改写（FileReferenceCounts），整行更新一律不写这一列。
        //   计数此前全是「读出来 ++ 再整行写回」，而 FileRecord 没有并发令牌：两个请求同时绑定同一份文件，
        //   都读到 1、都写回 2，实际持有者 3 个，之后两次释放就归零、文件被删；改名、改可见性、挪目录这类
        //   整行写回同样会把别人刚加上的计数覆盖回旧值。插入照常写入初值（BeforeSaveBehavior 不变）。
        //   与哨兵一样只是模型元数据，不进迁移快照，消费方零迁移。
        builder.Property(e => e.ReferenceCount).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        // 列默认值 false 只服务于绕过 EF 的直接 SQL 插入；EF 自己总是写入实际值。不写 ValueGeneratedNever 时，
        // 默认值与 CLR 默认值相同的 bool 列会让 EF 在模型校验时告警（它分不出「没设」与「设成 false」）。
        builder.Property(e => e.IsTemporary).HasDefaultFalse().ValueGeneratedNever();
        builder.Property(e => e.Tags).HasMaxLength(1024);

        builder.Property(e => e.Metadata).HasMaxLength(4096);

        // FileFolder relationship
        builder.HasIndex(e => e.FolderId);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.Md5Hash);
        builder.HasIndex(e => e.CreatorId);
        builder.HasIndex(e => e.CreationTime);
        builder.HasIndex(e => e.Provider);
    }
}

