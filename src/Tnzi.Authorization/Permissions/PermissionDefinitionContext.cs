namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限定义上下文实现
/// </summary>
public class PermissionDefinitionContext : IPermissionDefinitionContext
{
    private readonly Dictionary<string, PermissionGroupDefinition> _groups = new();
    private readonly Dictionary<string, PermissionDefinition> _permissions = new();

    /// <summary>
    /// 获取所有权限组
    /// </summary>
    public IReadOnlyDictionary<string, PermissionGroupDefinition> Groups => _groups;

    /// <summary>
    /// 获取所有权限
    /// </summary>
    public IReadOnlyDictionary<string, PermissionDefinition> Permissions => _permissions;

    public PermissionGroupDefinition AddGroup(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? defaultCategory = null)
    {
        Check.NotNullOrWhiteSpace(name);

        if (_groups.ContainsKey(name))
            return _groups[name];

        var group = new PermissionGroupDefinition
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            ParentName = parentName,
            IsEnabled = true,
            DefaultCategory = defaultCategory ?? PermissionCategory.Business
        };

        _groups[name] = group;
        return group;
    }

    public PermissionDefinition AddPermission(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? category = null)
    {
        Check.NotNullOrWhiteSpace(name);

        if (_permissions.ContainsKey(name))
            return _permissions[name];

        var permission = new PermissionDefinition
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            ParentName = parentName,
            IsEnabled = true,
            Category = category ?? ResolveInheritedCategory(parentName)
        };

        _permissions[name] = permission;
        return permission;
    }

    /// <summary>
    /// 未显式指定分类时的继承规则：parentName 指向组 → 组默认分类；
    /// 指向另一个权限（层级权限）→ 该权限的分类；否则 Business。
    /// </summary>
    private PermissionCategory ResolveInheritedCategory(string? parentName)
    {
        if (string.IsNullOrEmpty(parentName))
            return PermissionCategory.Business;
        if (_groups.TryGetValue(parentName, out var group))
            return group.DefaultCategory;
        if (_permissions.TryGetValue(parentName, out var parent))
            return parent.Category;
        return PermissionCategory.Business;
    }
}

