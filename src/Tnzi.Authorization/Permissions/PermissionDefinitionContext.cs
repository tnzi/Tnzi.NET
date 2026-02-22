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

    public PermissionGroupDefinition AddGroup(string name, string displayName, string? description = null, string? parentName = null)
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
            IsEnabled = true
        };

        _groups[name] = group;
        return group;
    }

    public PermissionDefinition AddPermission(string name, string displayName, string? description = null, string? parentName = null)
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
            IsEnabled = true
        };

        _permissions[name] = permission;
        return permission;
    }
}

