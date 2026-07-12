namespace Tnzi.Authorization.Dtos;

/// <summary>
/// Role-function assignment read DTO for the canonical list endpoint.
/// Denormalizes the function side (code / name / moduleId) so the admin
/// UI does not have to issue a second lookup per row.
/// </summary>
public class RoleFunctionDto
{
    /// <summary>
    /// Assignment ID (RoleFunction entity Id)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Role ID the function is assigned to
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Function ID
    /// </summary>
    public Guid FunctionId { get; set; }

    /// <summary>
    /// Function code (permission name)
    /// </summary>
    public string FunctionCode { get; set; } = string.Empty;

    /// <summary>
    /// Function display name
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Module ID the function belongs to
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// Whether the assignment is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Assignment creation time (when the function was assigned to the role)
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Paged query DTO for the canonical GET /admin/role-functions endpoint.
/// Supports optional filters on role / function / enabled state on top of
/// the framework-standard pagination + ordering inherited from PagedQueryDto.
/// </summary>
public class RoleFunctionQueryDto : PagedQueryDto
{
    /// <summary>
    /// Filter by role ID (optional)
    /// </summary>
    public Guid? RoleId { get; set; }

    /// <summary>
    /// Filter by function ID (optional)
    /// </summary>
    public Guid? FunctionId { get; set; }

    /// <summary>
    /// Filter by enabled state (optional)
    /// </summary>
    public bool? IsEnabled { get; set; }
}

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
    /// Total user-function direct-grant assignments
    /// </summary>
    public int TotalUserFunctionAssignments { get; set; }

    /// <summary>
    /// Number of enabled user-function direct-grant assignments
    /// </summary>
    public int EnabledUserFunctionAssignments { get; set; }
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
