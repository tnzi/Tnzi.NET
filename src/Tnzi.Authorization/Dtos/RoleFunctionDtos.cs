namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 分配功能请求
/// </summary>
public class AssignFunctionsRequest
{
    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 移除功能请求
/// </summary>
public class RemoveFunctionsRequest
{
    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 设置角色功能请求
/// </summary>
public class SetRoleFunctionsRequest
{
    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 批量分配功能请求
/// </summary>
public class BatchAssignFunctionsRequest
{
    /// <summary>
    /// 角色ID列表
    /// </summary>
    public IEnumerable<Guid> RoleIds { get; set; } = null!;

    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}
