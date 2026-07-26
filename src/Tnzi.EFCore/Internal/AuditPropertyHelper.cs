
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 审计属性辅助类
/// 负责自动填充审计字段（创建时间、创建人、修改时间、软删除等）
/// </summary>
internal static class AuditPropertyHelper
{
    /// <summary>
    /// 应用审计属性
    /// </summary>
    public static void ApplyAuditProperties(
        DbContext dbContext,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        TimeProvider? timeProvider = null,
        bool multiTenancyEnabled = false)
    {
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    IdGenerationHelper.ApplyAutoId(dbContext, entry);
                    if (entry.Entity is IHasCreationTime hasCreationTime && hasCreationTime.CreationTime == default)
                    {
                        hasCreationTime.CreationTime = utcNow;
                    }
                    if (entry.Entity is IHasCreator hasCreator && hasCreator.CreatorId == null)
                    {
                        hasCreator.CreatorId = currentUser?.Id;
                    }
                    // 无论是否启用租户隔离，都保留实体上的 TenantId 审计值。
                    // 当多租户关闭时，EF 模型可能忽略该列，但实体内存状态仍应保持一致。
                    if (entry.Entity is IMultiTenant multiTenant && multiTenant.TenantId == null)
                    {
                        multiTenant.TenantId = currentTenant?.Id ?? currentUser?.TenantId;
                    }
                    // 多租户启用时，IMultiTenant 实体的 TenantId 不应为 null（防止数据泄露）。
                    // 刻意只写调试输出（不接 ILogger）：本方法在每次 SaveChanges 对每个跟踪实体执行，
                    // 接日志会在批量操作中产生大量条目。注意 Debug.WriteLine 在 Release 构建中被编译移除，
                    // 因此该提示仅在开发期可见。
                    if (multiTenancyEnabled && entry.Entity is IMultiTenant mtEntity && mtEntity.TenantId == null)
                    {
                        var entityType = entry.Entity.GetType().Name;
                        Debug.WriteLine($"[MultiTenancy] Warning: Entity '{entityType}' created without TenantId while multi-tenancy is enabled. This may cause data isolation issues.");
                    }
                    if (entry.Entity is IConcurrencyStamp addedConcurrency)
                    {
                        addedConcurrency.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                    }
                    break;

                case EntityState.Modified:
                    if (entry.Entity is IHasModificationTime hasModTime)
                    {
                        hasModTime.LastModificationTime = utcNow;
                    }
                    if (entry.Entity is IHasModifier hasModifier)
                    {
                        hasModifier.LastModifierId = currentUser?.Id;
                    }
                    if (entry.Entity is IConcurrencyStamp modifiedConcurrency)
                    {
                        modifiedConcurrency.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                    }
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDelete softDelete)
                    {
                        entry.State = EntityState.Modified;
                        softDelete.IsDeleted = true;
                        if (entry.Entity is IHasDeleter hasDeleter)
                        {
                            hasDeleter.DeleterId = currentUser?.Id;
                            hasDeleter.DeletionTime = utcNow;
                        }
                        // 软删除也要更新修改时间和修改人
                        if (entry.Entity is IHasModificationTime hasModTimeOnDelete)
                        {
                            hasModTimeOnDelete.LastModificationTime = utcNow;
                        }
                        if (entry.Entity is IHasModifier hasModifierOnDelete)
                        {
                            hasModifierOnDelete.LastModifierId = currentUser?.Id;
                        }
                        if (entry.Entity is IConcurrencyStamp deletedConcurrency)
                        {
                            deletedConcurrency.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                        }
                    }
                    break;
            }
        }
    }
}
