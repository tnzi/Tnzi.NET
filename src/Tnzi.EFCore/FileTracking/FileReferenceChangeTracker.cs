
namespace Tnzi.EFCore.FileTracking;

// 注意：FileReferenceChangeType 和 FileReferenceChange 现在在 Tnzi 中定义

/// <summary>
/// 文件引用变更追踪器
/// 在 SaveChanges 之前收集所有带有 [FileField] 特性的属性变更
/// </summary>
public class FileReferenceChangeTracker
{
    private readonly List<FileReferenceChange> _changes = new();

    /// <summary>
    /// [FileField] 属性反射缓存，避免每次 CollectChanges 重复扫描
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _fileFieldCache = new();

    /// <summary>
    /// 从 ChangeTracker 收集文件引用变更
    /// </summary>
    public void CollectChanges(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State == EntityState.Unchanged)
                continue;

            var entityType = entry.Entity.GetType();
            var entityTypeName = entityType.Name;
            var entityId = GetEntityId(entry);

            if (string.IsNullOrEmpty(entityId))
                continue;

            // 获取标记了 FileField 的属性（使用缓存）
            var fileFieldProperties = _fileFieldCache.GetOrAdd(entityType, type =>
                type.GetProperties()
                    .Where(p => p.GetCustomAttribute<FileFieldAttribute>() != null)
                    .ToArray());

            if (fileFieldProperties.Length == 0)
                continue;

            foreach (var property in fileFieldProperties)
            {
                var attr = property.GetCustomAttribute<FileFieldAttribute>()!;
                var fieldName = property.Name;

                switch (entry.State)
                {
                    case EntityState.Added:
                        CollectAddedChanges(entry, property, attr, entityTypeName, entityId, fieldName);
                        break;
                    case EntityState.Modified:
                        CollectModifiedChanges(entry, property, attr, entityTypeName, entityId, fieldName);
                        break;
                    case EntityState.Deleted:
                        CollectDeletedChanges(entry, property, attr, entityTypeName, entityId, fieldName);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 收集新增实体的文件引用
    /// </summary>
    private void CollectAddedChanges(EntityEntry entry, PropertyInfo property, FileFieldAttribute attr,
        string entityType, string entityId, string fieldName)
    {
        var currentValue = property.GetValue(entry.Entity);
        var fileIds = ParseFileIds(currentValue, attr.Multiple);

        foreach (var fileId in fileIds)
        {
            _changes.Add(new FileReferenceChange
            {
                ChangeType = FileReferenceChangeType.Create,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                FileId = fileId
            });
        }
    }

    /// <summary>
    /// 收集修改实体的文件引用变更
    /// </summary>
    private void CollectModifiedChanges(EntityEntry entry, PropertyInfo property, FileFieldAttribute attr,
        string entityType, string entityId, string fieldName)
    {
        var propertyEntry = entry.Property(property.Name);
        if (!propertyEntry.IsModified)
            return;

        var originalValue = propertyEntry.OriginalValue;
        var currentValue = propertyEntry.CurrentValue;

        var originalFileIds = ParseFileIds(originalValue, attr.Multiple);
        var currentFileIds = ParseFileIds(currentValue, attr.Multiple);

        // 删除的文件引用
        var removedIds = originalFileIds.Except(currentFileIds).ToList();
        foreach (var fileId in removedIds)
        {
            _changes.Add(new FileReferenceChange
            {
                ChangeType = FileReferenceChangeType.Delete,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                FileId = fileId
            });
        }

        // 新增的文件引用
        var addedIds = currentFileIds.Except(originalFileIds).ToList();
        foreach (var fileId in addedIds)
        {
            _changes.Add(new FileReferenceChange
            {
                ChangeType = FileReferenceChangeType.Create,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                FileId = fileId
            });
        }
    }

    /// <summary>
    /// 收集删除实体的文件引用
    /// </summary>
    private void CollectDeletedChanges(EntityEntry entry, PropertyInfo property, FileFieldAttribute attr,
        string entityType, string entityId, string fieldName)
    {
        var originalValue = entry.Property(property.Name).OriginalValue;
        var fileIds = ParseFileIds(originalValue, attr.Multiple);

        foreach (var fileId in fileIds)
        {
            _changes.Add(new FileReferenceChange
            {
                ChangeType = FileReferenceChangeType.Delete,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                FileId = fileId
            });
        }
    }

    /// <summary>
    /// 获取所有变更
    /// </summary>
    public IReadOnlyList<FileReferenceChange> GetChanges() => _changes.AsReadOnly();

    /// <summary>
    /// 清空变更
    /// </summary>
    public void Clear() => _changes.Clear();

    /// <summary>
    /// 是否有变更
    /// </summary>
    public bool HasChanges => _changes.Count > 0;

    private static readonly List<Guid> EmptyGuidList = [];

    /// <summary>
    /// 解析文件ID
    /// </summary>
    private static List<Guid> ParseFileIds(object? value, bool isMultiple)
    {
        if (value == null)
            return EmptyGuidList;

        if (value is Guid singleId)
            return singleId != Guid.Empty ? [singleId] : EmptyGuidList;

        // 处理 Nullable<Guid>
        var nullableGuidType = Nullable.GetUnderlyingType(value.GetType());
        if (nullableGuidType == typeof(Guid))
        {
            var nullableId = (Guid?)value;
            if (nullableId.HasValue && nullableId.Value != Guid.Empty)
                return [nullableId.Value];
            return EmptyGuidList;
        }

        if (value is string strValue && !string.IsNullOrWhiteSpace(strValue))
        {
            if (isMultiple)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<Guid>>(strValue);
                    return list?.Where(g => g != Guid.Empty).ToList() ?? EmptyGuidList;
                }
                catch (JsonException)
                {
                    return strValue.Split(',')
                        .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                        .Where(g => g != Guid.Empty)
                        .ToList();
                }
            }
            else
            {
                if (Guid.TryParse(strValue, out var g) && g != Guid.Empty)
                    return [g];
            }
        }

        if (value is IEnumerable<Guid> multipleIds)
            return multipleIds.Where(g => g != Guid.Empty).ToList();

        return EmptyGuidList;
    }

    /// <summary>
    /// 获取实体ID（支持 Guid、long、int、string 等多种 ID 类型）
    /// </summary>
    private string GetEntityId(EntityEntry entry)
    {
        var idProperty = entry.Entity.GetType().GetProperty("Id");
        if (idProperty == null)
            return string.Empty;

        var idValue = idProperty.GetValue(entry.Entity);
        if (idValue == null)
            return string.Empty;

        return idValue switch
        {
            Guid guidId => guidId == Guid.Empty ? string.Empty : guidId.ToString(),
            long longId => longId == 0 ? string.Empty : longId.ToString(),
            int intId => intId == 0 ? string.Empty : intId.ToString(),
            string strId => strId,
            _ => idValue.ToString() ?? string.Empty
        };
    }
}
