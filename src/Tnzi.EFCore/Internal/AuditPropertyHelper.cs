
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 审计属性辅助类
/// 负责自动填充审计字段（创建时间、创建人、修改时间、软删除等）
/// </summary>
/// <remarks>
/// 逐实体的填充规则（<see cref="ApplyCreationAudit(object, ICurrentUser?, ICurrentTenant?, DateTime)"/> /
/// <see cref="ApplyModificationAudit(object, ICurrentUser?, DateTime)"/>）只有一份：<c>SaveChanges</c> 按变更跟踪器的状态套用，
/// 绕过变更跟踪器的 Dapper 批量插入 / 更新按「整批都是新增 / 整批都是修改」套用。
/// 两条路径此前各写各的，Dapper 那条压根没写 —— 主键解锁后一批 <c>CreationTime</c> 0001-01-01、
/// <c>TenantId</c> 为 null 的行安静落库。
/// </remarks>
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
                    ApplyCreationAudit(entry.Entity, currentUser, currentTenant, utcNow);
                    WarnIfTenantMissing(entry.Entity, multiTenancyEnabled);
                    break;

                case EntityState.Modified:
                    ApplyModificationAudit(entry.Entity, currentUser, utcNow);
                    // 直接把 IsDeleted 置 true 的软删除（不经仓储 DeleteAsync，实体停在 Modified）
                    // 同样要留下删除人与删除时间。
                    //
                    // ★ 为什么必须在这里补：下面的 Deleted 分支是仓储删除路径，它把 EF 的删除
                    //   转成软删并写 deleter/deletionTime。但代码也可以**直接赋值** IsDeleted = true
                    //   —— `ChildCollectionSync.ReplaceChildren` 就是这么做的（它是纯内存操作，
                    //   拿不到当前用户，本来也不该拿）。那条路径此前只走到上面几行，结果是一批
                    //   「没有人、在没有时间删掉的」软删行：IsDeleted 为真，DeleterId 与
                    //   DeletionTime 却是 null，而这正是事后追责时唯一想看的两列。
                    //
                    //   判据是**跃迁**（原值 false → 现值 true）而不是「现在为 true」：后者会在
                    //   每次修改一条已软删的行时反复覆盖删除人，把最初那次删除的痕迹抹掉。
                    if (entry.Entity is ISoftDelete { IsDeleted: true } and IHasDeleter softDeletedInPlace
                        && WasNotDeletedBefore(entry))
                    {
                        softDeletedInPlace.DeleterId = currentUser?.Id;
                        softDeletedInPlace.DeletionTime = utcNow;
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
                        // 软删除也要更新修改时间和修改人（含换并发戳）
                        ApplyModificationAudit(entry.Entity, currentUser, utcNow);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 新增实体的创建审计：创建时间（仍为默认值时）、创建人（仍为 null 时）、租户（仍为 null 时）、并发戳（总是换新）。
    /// 调用方已经赋的值一律保留。
    /// </summary>
    public static void ApplyCreationAudit(object entity, ICurrentUser? currentUser, ICurrentTenant? currentTenant, DateTime utcNow)
    {
        if (entity is IHasCreationTime hasCreationTime && hasCreationTime.CreationTime == default)
        {
            hasCreationTime.CreationTime = utcNow;
        }
        if (entity is IHasCreator hasCreator && hasCreator.CreatorId == null)
        {
            hasCreator.CreatorId = currentUser?.Id;
        }
        // 无论是否启用租户隔离，都保留实体上的 TenantId 审计值。
        // 当多租户关闭时，EF 模型可能忽略该列，但实体内存状态仍应保持一致。
        if (entity is IMultiTenant multiTenant && multiTenant.TenantId == null)
        {
            multiTenant.TenantId = currentTenant?.Id ?? currentUser?.TenantId;
        }
        if (entity is IConcurrencyStamp concurrency)
        {
            concurrency.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// 修改实体的修改审计：修改时间、修改人、并发戳（总是换新）。
    /// </summary>
    public static void ApplyModificationAudit(object entity, ICurrentUser? currentUser, DateTime utcNow)
    {
        if (entity is IHasModificationTime hasModTime)
        {
            hasModTime.LastModificationTime = utcNow;
        }
        if (entity is IHasModifier hasModifier)
        {
            hasModifier.LastModifierId = currentUser?.Id;
        }
        if (entity is IConcurrencyStamp concurrency)
        {
            concurrency.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// 绕过变更跟踪器的批量插入：对每个实体套用与 <c>SaveChanges</c> 新增分支同一套创建审计，
    /// 协作者取自上下文自身（见 <see cref="IAuditPropertyContext"/>）。Id 生成不在这里（调用方按主键策略决定）。
    /// </summary>
    public static void ApplyCreationAudit(DbContext dbContext, IEnumerable<object> entities)
    {
        Check.NotNull(dbContext);
        Check.NotNull(entities);

        var (currentUser, currentTenant, timeProvider) = ResolveCollaborators(dbContext);
        var multiTenancyEnabled = dbContext is IMultiTenancySwitchProvider provider && provider.IsMultiTenancyEnabled;
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        foreach (var entity in entities)
        {
            ApplyCreationAudit(entity, currentUser, currentTenant, utcNow);
            WarnIfTenantMissing(entity, multiTenancyEnabled);
        }
    }

    /// <summary>
    /// 绕过变更跟踪器的批量更新：对每个实体套用与 <c>SaveChanges</c> 修改分支同一套修改审计。
    /// </summary>
    public static void ApplyModificationAudit(DbContext dbContext, IEnumerable<object> entities)
    {
        Check.NotNull(dbContext);
        Check.NotNull(entities);

        var (currentUser, _, timeProvider) = ResolveCollaborators(dbContext);
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        foreach (var entity in entities)
        {
            ApplyModificationAudit(entity, currentUser, utcNow);
        }
    }

    /// <summary>
    /// 上下文实现了 <see cref="IAuditPropertyContext"/>（两个框架基类都实现）就取它自己的那组协作者；
    /// 否则退到 options 携带的应用容器（手工构造的裸 DbContext 没有容器 ⇒ 全部为 null，审计值留空，
    /// 与该上下文自己的 SaveChanges 行为一致：裸 DbContext 本来就不填审计）。
    /// </summary>
    private static (ICurrentUser? CurrentUser, ICurrentTenant? CurrentTenant, TimeProvider? TimeProvider) ResolveCollaborators(DbContext dbContext)
    {
        if (dbContext is IAuditPropertyContext auditContext)
        {
            return (auditContext.CurrentUser, auditContext.CurrentTenant, auditContext.TimeProvider);
        }

        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        return (
            serviceProvider?.GetService<ICurrentUser>(),
            serviceProvider?.GetService<ICurrentTenant>(),
            serviceProvider?.GetService<TimeProvider>());
    }

    /// <summary>
    /// 多租户启用时，IMultiTenant 实体的 TenantId 不应为 null（防止数据泄露）。
    /// 刻意只写调试输出（不接 ILogger）：本方法对每个新增实体执行，接日志会在批量操作中产生大量条目。
    /// 注意 Debug.WriteLine 在 Release 构建中被编译移除，因此该提示仅在开发期可见。
    /// </summary>
    private static void WarnIfTenantMissing(object entity, bool multiTenancyEnabled)
    {
        if (multiTenancyEnabled && entity is IMultiTenant { TenantId: null })
        {
            var entityType = entity.GetType().Name;
            Debug.WriteLine($"[MultiTenancy] Warning: Entity '{entityType}' created without TenantId while multi-tenancy is enabled. This may cause data isolation issues.");
        }
    }

    /// <summary>
    /// 这条实体在本次变更之前是否<b>未被</b>软删除。
    /// </summary>
    /// <remarks>
    /// 用于把「刚刚被软删」与「本来就是软删状态、这次只是改了别的字段」区分开：
    /// 只有前者该写删除人与删除时间，后者写了就会把最初那次删除的痕迹抹掉。
    /// <para>
    /// 拿不到原值时（实体未被跟踪过原始快照）保守地认为<b>之前已删</b>，即不写 ——
    /// 宁可少写一次，也不要覆盖掉一条真实的删除记录。
    /// </para>
    /// </remarks>
    private static bool WasNotDeletedBefore(EntityEntry entry)
    {
        var property = entry.Properties.FirstOrDefault(
            p => p.Metadata.Name == nameof(ISoftDelete.IsDeleted));

        return property?.OriginalValue is false;
    }
}
