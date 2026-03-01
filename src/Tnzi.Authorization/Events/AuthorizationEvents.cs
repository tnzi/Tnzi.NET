namespace Tnzi.Authorization.Events;

/// <summary>
/// 角色功能权限变更事件
/// 当角色的功能权限被分配、移除或重置时触发
/// </summary>
public class RoleFunctionsChangedEvent : EventBase
{
    /// <summary>
    /// 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 变更类型
    /// </summary>
    public PermissionChangeType ChangeType { get; set; }

    /// <summary>
    /// 受影响的功能ID列表
    /// </summary>
    public List<Guid> AffectedFunctionIds { get; set; } = [];
}

/// <summary>
/// 模块启用/禁用事件
/// 当模块被启用或禁用时触发（包括级联操作）
/// </summary>
public class ModuleEnabledChangedEvent : EventBase
{
    /// <summary>
    /// 根模块ID（触发操作的模块）
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 模块Code
    /// </summary>
    public string ModuleCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 受级联影响的所有模块ID（包括根模块）
    /// </summary>
    public List<Guid> AffectedModuleIds { get; set; } = [];
}

/// <summary>
/// 功能启用/禁用事件
/// 当单个功能被启用或禁用时触发
/// </summary>
public class FunctionEnabledChangedEvent : EventBase
{
    /// <summary>
    /// 功能ID
    /// </summary>
    public Guid FunctionId { get; set; }

    /// <summary>
    /// 功能Code
    /// </summary>
    public string FunctionCode { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }
}

/// <summary>
/// 权限变更类型
/// </summary>
public enum PermissionChangeType
{
    /// <summary>
    /// 分配（新增权限）
    /// </summary>
    Assigned,

    /// <summary>
    /// 移除（删除权限）
    /// </summary>
    Removed,

    /// <summary>
    /// 重置（清除并重新设置）
    /// </summary>
    Reset,

    /// <summary>
    /// 清空（删除所有权限）
    /// </summary>
    Cleared
}
