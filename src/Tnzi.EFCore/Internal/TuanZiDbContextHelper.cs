
namespace Tnzi.EFCore.Internal;

/// <summary>
/// DbContext 内部共享逻辑辅助类（门面）
/// 用于统一 TnziDbContext 和 IdentityDbContext 的底层实现
/// 实际逻辑委托给专职 Helper 类：
/// - EntityRegistrationHelper：实体发现与注册
/// - AuditPropertyHelper：审计属性填充
/// - IdGenerationHelper：ID 自动生成
/// - DbContextServiceResolver：服务解析
/// </summary>
public static class TnziDbContextHelper
{
    /// <summary>
    /// 初始化模型（配置实体注册和批量配置）
    /// </summary>
    public static void OnModelCreating(DbContext dbContext, ModelBuilder modelBuilder, Action<ModelBuilder>? additionalConfigurations = null)
    {
        EntityRegistrationHelper.RegisterEntities(dbContext, modelBuilder, additionalConfigurations);
    }

    /// <summary>
    /// 统一异步保存逻辑
    /// </summary>
    public static async Task<int> SaveChangesAsync(
        DbContext dbContext,
        Func<CancellationToken, Task<int>> baseSaveAsync,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null,
        bool multiTenancyEnabled = false)
    {
        var logger = DbContextServiceResolver.GetLogger(dbContext);

        // 1. 应用审计
        AuditPropertyHelper.ApplyAuditProperties(dbContext, currentUser, currentTenant, timeProvider, multiTenancyEnabled);

        // 2. 文件引用追踪 - 收集变更信息
        var fileTracker = new FileTracking.FileReferenceChangeTracker();
        fileTracker.CollectChanges(dbContext.ChangeTracker);

        logger?.LogDebug("[FileTracking] Collected changes: HasChanges={HasChanges}, Count={Count}",
            fileTracker.HasChanges, fileTracker.GetChanges().Count);

        // 3. 保存主实体
        var result = await baseSaveAsync(cancellationToken);

        // 4. 收集并发布领域事件（保存成功后收集，避免保存失败时事件丢失）
        var domainEvents = HarvestDomainEvents(dbContext);
        if (domainEvents.Count > 0)
        {
            await DispatchDomainEventsAsync(dbContext, domainEvents, cancellationToken);
        }

        // 6. 文件引用处理（事务后异步处理，避免并发 DbContext 问题）
        if (fileTracker.HasChanges)
        {
            foreach (var change in fileTracker.GetChanges())
            {
                logger?.LogInformation("[FileTracking] Change detected: {ChangeType} - Entity={EntityType}/{EntityId}, Field={FieldName}, FileId={FileId}",
                    change.ChangeType, change.EntityType, change.EntityId, change.FieldName, change.FileId);
            }

            var processor = DbContextServiceResolver.GetFileReferenceProcessor(dbContext);
            if (processor != null)
            {
                try
                {
                    // 在主保存完成后处理文件引用
                    await processor.ProcessChangesAsync(fileTracker.GetChanges(), cancellationToken);

                    // 显式保存 Repository 延迟添加的实体
                    // 使用 baseSaveAsync 避开递归审计逻辑
                    if (dbContext.ChangeTracker.HasChanges())
                    {
                        await baseSaveAsync(cancellationToken);
                        logger?.LogDebug("[FileTracking] Saved pending file reference changes");
                    }

                    logger?.LogInformation("[FileTracking] Processed {Count} file reference changes", fileTracker.GetChanges().Count);

                    // 发布删除事件
                    await processor.PublishDeleteEventsAsync(fileTracker.GetChanges(), cancellationToken);
                }
                catch (Exception ex)
                {
                    // 文件引用处理失败不应影响主业务
                    logger?.LogError(ex, "[FileTracking] Error processing file references");
                }
            }
            else
            {
                logger?.LogWarning("[FileTracking] IFileReferenceProcessor not found! File references will NOT be tracked.");
            }
        }

        return result;
    }

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
        AuditPropertyHelper.ApplyAuditProperties(dbContext, currentUser, currentTenant, timeProvider, multiTenancyEnabled);
    }

    /// <summary>
    /// 从跟踪的实体中收集领域事件并清除
    /// </summary>
    private static List<IEvent> HarvestDomainEvents(DbContext dbContext)
    {
        var events = new List<IEvent>();
        foreach (var entry in dbContext.ChangeTracker.Entries<IHasDomainEvents>())
        {
            var domainEvents = entry.Entity.DomainEvents;
            if (domainEvents.Count > 0)
            {
                events.AddRange(domainEvents);
                entry.Entity.ClearDomainEvents();
            }
        }
        return events;
    }

    /// <summary>
    /// 发布领域事件（事务感知：事务中延迟到提交后发布）
    /// </summary>
    private static async Task DispatchDomainEventsAsync(DbContext dbContext, List<IEvent> events, CancellationToken cancellationToken)
    {
        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        if (serviceProvider == null) return;

        var eventBus = serviceProvider.GetService<IEventBus>();
        if (eventBus == null) return;

        var postCommitQueue = serviceProvider.GetService<IPostCommitActionQueue>();
        var uowManager = serviceProvider.GetService<IUnitOfWorkManager>();

        foreach (var @event in events)
        {
            if (uowManager?.IsEnabledTransaction == true && postCommitQueue != null)
            {
                var capturedEvent = @event;
                postCommitQueue.Enqueue(async ct => await eventBus.PublishAsync(capturedEvent, ct));
            }
            else
            {
                await eventBus.PublishAsync(@event, cancellationToken);
            }
        }
    }
}
