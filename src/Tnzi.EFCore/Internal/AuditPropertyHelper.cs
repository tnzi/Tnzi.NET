
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
    public static void ApplyAuditProperties(DbContext dbContext, ICurrentUser currentUser, ICurrentTenant? currentTenant)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    IdGenerationHelper.ApplyAutoId(dbContext, entry);
                    if (entry.Entity is IHasCreationTime hasCreationTime && hasCreationTime.CreationTime == default)
                    {
                        hasCreationTime.CreationTime = DateTime.UtcNow;
                    }
                    if (entry.Entity is IHasCreator hasCreator && hasCreator.CreatorId == null)
                    {
                        hasCreator.CreatorId = currentUser?.Id;
                    }
                    if (entry.Entity is IMultiTenant multiTenant && multiTenant.TenantId == null)
                    {
                        multiTenant.TenantId = currentTenant?.Id ?? currentUser?.TenantId;
                    }
                    break;

                case EntityState.Modified:
                    if (entry.Entity is IHasModificationTime hasModTime)
                    {
                        hasModTime.LastModificationTime = DateTime.UtcNow;
                    }
                    if (entry.Entity is IHasModifier hasModifier)
                    {
                        hasModifier.LastModifierId = currentUser?.Id;
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
                            hasDeleter.DeletionTime = DateTime.UtcNow;
                        }
                        // 软删除也要更新修改时间和修改人
                        if (entry.Entity is IHasModificationTime hasModTimeOnDelete)
                        {
                            hasModTimeOnDelete.LastModificationTime = DateTime.UtcNow;
                        }
                        if (entry.Entity is IHasModifier hasModifierOnDelete)
                        {
                            hasModifierOnDelete.LastModifierId = currentUser?.Id;
                        }
                    }
                    break;
            }
        }
    }
}
