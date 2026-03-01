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

/// <summary>
/// Permission holder information (role that has a specific permission)
/// </summary>
public class PermissionRoleDto
{
    /// <summary>
    /// Role ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Whether the role-function assignment is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Function ID
    /// </summary>
    public Guid FunctionId { get; set; }

    /// <summary>
    /// Function code (permission name)
    /// </summary>
    public string FunctionCode { get; set; } = string.Empty;

    /// <summary>
    /// Function name
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;
}

/// <summary>
/// Permission user information (user that has a specific permission via role)
/// </summary>
public class PermissionUserDto
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Role IDs through which the user has this permission
    /// </summary>
    public List<Guid> RoleIds { get; set; } = new();
}

/// <summary>
/// Authorization statistics overview
/// </summary>
public class AuthorizationStatisticsDto
{
    /// <summary>
    /// Total number of modules
    /// </summary>
    public int TotalModules { get; set; }

    /// <summary>
    /// Number of enabled modules
    /// </summary>
    public int EnabledModules { get; set; }

    /// <summary>
    /// Total number of functions (permissions)
    /// </summary>
    public int TotalFunctions { get; set; }

    /// <summary>
    /// Number of enabled functions
    /// </summary>
    public int EnabledFunctions { get; set; }

    /// <summary>
    /// Total role-function assignments
    /// </summary>
    public int TotalRoleFunctionAssignments { get; set; }

    /// <summary>
    /// Number of enabled role-function assignments
    /// </summary>
    public int EnabledRoleFunctionAssignments { get; set; }

    /// <summary>
    /// Total module-role assignments
    /// </summary>
    public int TotalModuleRoleAssignments { get; set; }

    /// <summary>
    /// Total module-user assignments
    /// </summary>
    public int TotalModuleUserAssignments { get; set; }
}

/// <summary>
/// Permission comparison result between two roles
/// </summary>
public class PermissionComparisonDto
{
    /// <summary>
    /// First role ID
    /// </summary>
    public Guid RoleId1 { get; set; }

    /// <summary>
    /// Second role ID
    /// </summary>
    public Guid RoleId2 { get; set; }

    /// <summary>
    /// Functions only in role 1 (not in role 2)
    /// </summary>
    public List<FunctionSummaryDto> OnlyInRole1 { get; set; } = [];

    /// <summary>
    /// Functions only in role 2 (not in role 1)
    /// </summary>
    public List<FunctionSummaryDto> OnlyInRole2 { get; set; } = [];

    /// <summary>
    /// Functions shared by both roles
    /// </summary>
    public List<FunctionSummaryDto> Shared { get; set; } = [];
}

/// <summary>
/// Minimal function info for comparison results
/// </summary>
public class FunctionSummaryDto
{
    /// <summary>
    /// Function ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Function code (permission name)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Function name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module code
    /// </summary>
    public string? ModuleCode { get; set; }
}

/// <summary>
/// Exported role permission data (portable, uses function codes)
/// </summary>
public class RolePermissionExportDto
{
    /// <summary>
    /// Export format version
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Export timestamp
    /// </summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>
    /// Source role ID (informational only, not used during import)
    /// </summary>
    public Guid? SourceRoleId { get; set; }

    /// <summary>
    /// Function codes assigned to the role
    /// </summary>
    public List<string> FunctionCodes { get; set; } = [];
}

/// <summary>
/// Result of permission import operation
/// </summary>
public class PermissionImportResultDto
{
    /// <summary>
    /// Number of permissions successfully imported (new assignments)
    /// </summary>
    public int Imported { get; set; }

    /// <summary>
    /// Number of permissions already existing (skipped)
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Function codes that were not found in the system
    /// </summary>
    public List<string> NotFound { get; set; } = [];
}

/// <summary>
/// Request to clone role permissions
/// </summary>
public class CloneRolePermissionsRequest
{
    /// <summary>
    /// Source role ID to clone from
    /// </summary>
    public Guid SourceRoleId { get; set; }
}

/// <summary>
/// Request to compare two roles
/// </summary>
public class CompareRolesRequest
{
    /// <summary>
    /// First role ID
    /// </summary>
    public Guid RoleId1 { get; set; }

    /// <summary>
    /// Second role ID
    /// </summary>
    public Guid RoleId2 { get; set; }
}
