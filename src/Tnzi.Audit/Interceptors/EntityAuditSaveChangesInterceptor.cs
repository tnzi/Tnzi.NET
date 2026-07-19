// EntityState 在 EF Core 与本模块 Metadata 命名空间中同名，文件级别名消歧
using System.Collections.Concurrent;
using AuditEntityState = Tnzi.Audit.Metadata.EntityState;
using EfEntityState = Microsoft.EntityFrameworkCore.EntityState;

namespace Tnzi.Audit.Interceptors;

/// <summary>
/// 实体级审计 SaveChanges 拦截器。
/// <para>
/// 经 Tnzi.EFCore 的 IInterceptor seam 挂进所有 Tnzi DbContext（仅 Audit 模块加载时注册）：
/// SavingChanges 阶段快照 ChangeTracker 中 Added/Modified/Deleted 实体的属性变更，
/// SavedChanges 阶段定稿（Added 实体的数据库生成主键此时才有值）并写入 per-request
/// 的 <see cref="IEntityAuditCollector"/>；保存失败则丢弃本次快照。
/// </para>
/// <para>
/// 仅在「会产出 AuditOperation 的 HTTP 请求」中采集（实体变更最终挂在请求级
/// AuditOperation 上）：后台任务等无宿主场景、操作审计关闭、命中排除路径或
/// [AuditDisabled] 端点均直接跳过（经 <see cref="AuditOperationGate"/> 与
/// AuditMiddleware 共用同一判定），零采集开销；
/// <see cref="AuditOptions.EnableEntityAudit"/> 经 IOptionsMonitor 热读，关闭时同样零开销。
/// </para>
/// <para>
/// 注意：采集发生在 SaveChanges 成功时，外层工作单元事务若最终回滚，
/// 条目仍会记录（此时操作审计的 ResultType 为 Failed，可据此甄别）。
/// </para>
/// </summary>
public class EntityAuditSaveChangesInterceptor : SaveChangesInterceptor
{
    // 列长度上限与实体配置保持一致
    // （见 Entities/Configs/AuditEntityEntryConfiguration / AuditPropertyEntryConfiguration）
    private const int EntityTypeNameMaxLength = 200;
    private const int EntityTypeFullNameMaxLength = 500;
    private const int EntityIdMaxLength = 100;
    private const int PropertyNameMaxLength = 200;
    private const int PropertyTypeNameMaxLength = 200;

    // OriginalValue/NewValue 列本身无长度上限（TEXT 列），此处设采集上限，
    // 防止超长字段（富文本/大 JSON）把审计表撑爆——超出部分截断。
    private const int PropertyValueMaxLength = 4000;

    // 单次 SaveChanges 采集的实体条目上限（防御批量导入等极端场景把单条
    // AuditOperation 的实体图撑爆）。超出部分丢弃并记 Warning 日志，不静默。
    private const int MaxEntriesPerSave = 500;

    /// <summary>
    /// 排除的实体类型（自审计实体防递归 + 纯技术基础设施实体防噪音）：
    /// Audit 三表由审计管道自身写入；OutboxMessage 是事件外发中转行（负载可能含敏感数据）；
    /// DocumentSequence 是连续编号计数器行（每次取号 +1）。
    /// </summary>
    private static readonly HashSet<string> ExcludedEntityTypes = new(StringComparer.Ordinal)
    {
        typeof(AuditOperation).FullName!,
        typeof(AuditEntityEntry).FullName!,
        typeof(AuditPropertyEntry).FullName!,
        typeof(OutboxMessage).FullName!,
        typeof(DocumentSequence).FullName!,
    };

    // [AuditIgnore] 声明式豁免（类级=整实体不采集，属性级=该属性值不记录）的
    // 按 CLR 类型反射元数据缓存。敏感值字段（令牌/密钥）靠它精确豁免——
    // SensitiveFields 按属性名跨实体匹配，"Value" 这类通用名无法进名单。
    private static readonly ConcurrentDictionary<Type, AuditIgnoreInfo> IgnoreInfoCache = new();

    private sealed record AuditIgnoreInfo(bool EntityIgnored, HashSet<string> IgnoredProperties);

    private static AuditIgnoreInfo GetIgnoreInfo(Type clrType)
        => IgnoreInfoCache.GetOrAdd(clrType, static type =>
        {
            var ignoredProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in type.GetProperties())
            {
                if (property.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
                {
                    ignoredProperties.Add(property.Name);
                }
            }

            var entityIgnored = type.IsDefined(typeof(AuditIgnoreAttribute), inherit: true);
            return new AuditIgnoreInfo(entityIgnored, ignoredProperties);
        });

    private readonly IEntityAuditCollector _collector;
    private readonly IOptionsMonitor<AuditOptions> _optionsMonitor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<EntityAuditSaveChangesInterceptor> _logger;

    // SavingChanges → SavedChanges 之间的待定稿快照。拦截器是 Scoped 的，
    // 同一 scope 下多个 DbContext 共享同一实例，故按 DbContext 分键；
    // 加锁防御同 scope 内不同 DbContext 并行 SaveChanges（与 collector 同一防御口径）。
    private readonly object _pendingLock = new();
    private readonly Dictionary<DbContext, List<PendingCapture>> _pending = [];

    public EntityAuditSaveChangesInterceptor(
        IEntityAuditCollector collector,
        IOptionsMonitor<AuditOptions> optionsMonitor,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EntityAuditSaveChangesInterceptor> logger)
    {
        _collector = Check.NotNull(collector);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _logger = Check.NotNull(logger);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CaptureChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FinalizeCapture(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        FinalizeCapture(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        DiscardCapture(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardCapture(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// SavingChanges 阶段：快照当前变更。Added 实体的最终主键在保存后才可靠
    /// （数据库生成键场景），故先入待定稿列表，SavedChanges 再定稿 EntityId。
    /// </summary>
    private void CaptureChanges(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        var options = _optionsMonitor.CurrentValue;
        if (!options.EnableEntityAudit)
        {
            return;
        }

        // 非 HTTP 场景（后台任务等）没有 AuditOperation 承载，直接跳过采集；
        // 本请求不会产出 AuditOperation 时（操作审计关闭 / 命中排除路径 / [AuditDisabled]）
        // 采集注定被丢弃，同样跳过省掉快照开销（与 AuditMiddleware 共用 AuditOperationGate 判定）。
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null || !AuditOperationGate.ShouldAudit(httpContext, options))
        {
            return;
        }

        List<PendingCapture>? captures = null;
        var skipped = 0;

        foreach (var efEntry in context.ChangeTracker.Entries())
        {
            if (efEntry.State is not (EfEntityState.Added or EfEntityState.Modified or EfEntityState.Deleted))
            {
                continue;
            }

            var clrType = efEntry.Metadata.ClrType;
            if (ExcludedEntityTypes.Contains(clrType.FullName ?? clrType.Name))
            {
                continue;
            }

            // 类级 [AuditIgnore]：整个实体类型豁免采集
            if (GetIgnoreInfo(clrType).EntityIgnored)
            {
                continue;
            }

            if (captures?.Count >= MaxEntriesPerSave)
            {
                skipped++;
                continue;
            }

            var auditEntry = CaptureEntity(efEntry, options);
            if (auditEntry == null)
            {
                continue;
            }

            captures ??= [];
            captures.Add(new PendingCapture(auditEntry, efEntry, efEntry.State == EfEntityState.Added));
        }

        if (skipped > 0)
        {
            // Warning 级：截断意味着审计数据真实缺失，生产默认日志级别必须可见
            _logger.LogWarning(
                "Entity audit capture truncated: {Skipped} entries beyond the per-save limit of {Limit} were dropped.",
                skipped, MaxEntriesPerSave);
        }

        if (captures != null)
        {
            lock (_pendingLock)
            {
                _pending[context] = captures;
            }
        }
    }

    /// <summary>
    /// SavedChanges 阶段：定稿 Added 实体的 EntityId（此时数据库生成的主键已回填），
    /// 并把本次快照写入 per-request collector。
    /// </summary>
    private void FinalizeCapture(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        List<PendingCapture>? captures;
        lock (_pendingLock)
        {
            if (!_pending.Remove(context, out captures))
            {
                return;
            }
        }

        foreach (var capture in captures)
        {
            if (capture.NeedsKeyFixup)
            {
                capture.AuditEntry.EntityId = BuildEntityId(capture.EfEntry);
            }
        }

        _collector.AddRange(captures.Select(c => c.AuditEntry));
    }

    /// <summary>保存失败：本次变更未落库，丢弃快照。</summary>
    private void DiscardCapture(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending.Remove(context);
        }
    }

    /// <summary>
    /// 采集单个实体的变更条目。Added 记录非空新值、Deleted 记录非空原值、
    /// Modified 只记录真正发生变化的属性（经 ValueComparer 比较原值与新值）。
    /// 主键不进属性列表（已由 EntityId 承载）。
    /// </summary>
    private AuditEntityEntry? CaptureEntity(EntityEntry efEntry, AuditOptions options)
    {
        var state = efEntry.State switch
        {
            EfEntityState.Added => AuditEntityState.Added,
            EfEntityState.Modified => AuditEntityState.Modified,
            EfEntityState.Deleted => AuditEntityState.Deleted,
            _ => (AuditEntityState?)null
        };
        if (state == null)
        {
            return null;
        }

        var clrType = efEntry.Metadata.ClrType;
        var ignoreInfo = GetIgnoreInfo(clrType);
        var utcNow = DateTime.UtcNow;
        var auditEntry = new AuditEntityEntry
        {
            EntityTypeName = Truncate(clrType.Name, EntityTypeNameMaxLength),
            EntityTypeFullName = Truncate(clrType.FullName ?? clrType.Name, EntityTypeFullNameMaxLength),
            EntityId = BuildEntityId(efEntry),
            OperationType = state.Value,
            CreationTime = utcNow
        };

        foreach (var property in efEntry.Properties)
        {
            var metadata = property.Metadata;
            if (metadata.IsPrimaryKey())
            {
                continue;
            }

            // 属性级 [AuditIgnore]：敏感值字段（令牌/密钥等）完全不进审计行
            if (ignoreInfo.IgnoredProperties.Contains(metadata.Name))
            {
                continue;
            }

            string? originalValue = null;
            string? newValue = null;

            switch (state.Value)
            {
                case AuditEntityState.Added:
                    if (property.CurrentValue == null)
                    {
                        continue;
                    }
                    newValue = FormatValue(property.CurrentValue);
                    break;

                case AuditEntityState.Deleted:
                    if (property.OriginalValue == null)
                    {
                        continue;
                    }
                    originalValue = FormatValue(property.OriginalValue);
                    break;

                case AuditEntityState.Modified:
                    if (!property.IsModified)
                    {
                        continue;
                    }
                    // IsModified 只表示"被标记为写回"，值可能并未变化（如显式标脏）；
                    // 经 ValueComparer 结构化比较（byte[] 等引用类型不可用 Equals）
                    var comparer = metadata.GetValueComparer();
                    var unchanged = comparer != null
                        ? comparer.Equals(property.OriginalValue, property.CurrentValue)
                        : Equals(property.OriginalValue, property.CurrentValue);
                    if (unchanged)
                    {
                        continue;
                    }
                    originalValue = FormatValue(property.OriginalValue);
                    newValue = FormatValue(property.CurrentValue);
                    break;
            }

            // 敏感字段脱敏：字段名与 SensitiveFields 大小写不敏感匹配时以掩码入库，
            // 与 RequestBodyRedactor 的请求体脱敏语义一致（同一掩码）
            if (options.SensitiveFields.Contains(metadata.Name))
            {
                if (originalValue != null)
                {
                    originalValue = RequestBodyRedactor.RedactedValue;
                }
                if (newValue != null)
                {
                    newValue = RequestBodyRedactor.RedactedValue;
                }
            }

            auditEntry.PropertyEntries.Add(new AuditPropertyEntry
            {
                PropertyName = Truncate(metadata.Name, PropertyNameMaxLength),
                PropertyTypeName = Truncate(GetFriendlyTypeName(metadata.ClrType), PropertyTypeNameMaxLength),
                OriginalValue = originalValue,
                NewValue = newValue,
                CreationTime = utcNow
            });
        }

        // Modified 但无任何真实属性变化（如仅显式标脏）→ 无审计价值，跳过
        if (state == AuditEntityState.Modified && auditEntry.PropertyEntries.Count == 0)
        {
            return null;
        }

        return auditEntry;
    }

    /// <summary>主键值拼为字符串（复合键以逗号连接）。</summary>
    private static string? BuildEntityId(EntityEntry efEntry)
    {
        var primaryKey = efEntry.Metadata.FindPrimaryKey();
        if (primaryKey == null)
        {
            return null;
        }

        var text = string.Join(",", primaryKey.Properties.Select(p => efEntry.Property(p.Name).CurrentValue?.ToString()));
        return string.IsNullOrEmpty(text) ? null : Truncate(text, EntityIdMaxLength);
    }

    /// <summary>属性值转为可读字符串（时间用 ISO 8601、数值用不变文化，复杂类型回退 JSON）。</summary>
    private static string? FormatValue(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            Guid g => g.ToString(),
            Enum e => e.ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => SerializeFallback(value)
        };

        return text == null ? null : Truncate(text, PropertyValueMaxLength);
    }

    private static string? SerializeFallback(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (Exception)
        {
            return value.ToString();
        }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying != null ? underlying.Name + "?" : type.Name;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    /// <param name="AuditEntry">已快照的审计条目</param>
    /// <param name="EfEntry">EF 变更条目引用（用于保存后定稿主键）</param>
    /// <param name="NeedsKeyFixup">Added 实体需在 SavedChanges 后重算 EntityId</param>
    private sealed record PendingCapture(AuditEntityEntry AuditEntry, EntityEntry EfEntry, bool NeedsKeyFixup);
}
