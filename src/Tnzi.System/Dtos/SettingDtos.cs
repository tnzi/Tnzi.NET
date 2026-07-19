namespace Tnzi.System.Dtos;

/// <summary>
/// 系统配置输出
/// </summary>
public class SettingDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool IsSystem { get; set; }
    public bool IsEncrypted { get; set; }
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
    public SettingScope Scope { get; set; }
    public string? ScopeId { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建系统配置DTO
/// </summary>
public class CreateSettingDto
{
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = null!;

    [Required]
    // 64KB：与实体 Value 列（已移除长度上限，映射 text/nvarchar(max)）对齐；
    // 特大值（如主题快照 64KB）走专用服务写入，通用 CRUD 仍保留有界上限防滥用。
    [MaxLength(65536)]
    public string Value { get; set; } = null!;

    public string? Description { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
    public SettingScope Scope { get; set; } = SettingScope.Global;
    public string? ScopeId { get; set; }
}

/// <summary>
/// 更新系统配置DTO
/// </summary>
public class UpdateSettingDto
{
    [Required]
    // 64KB：与实体 Value 列（已移除长度上限，映射 text/nvarchar(max)）对齐；
    // 特大值（如主题快照 64KB）走专用服务写入，通用 CRUD 仍保留有界上限防滥用。
    [MaxLength(65536)]
    public string Value { get; set; } = null!;

    public string? Description { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
}

/// <summary>
/// 设置加密配置值 DTO
/// </summary>
public class SetEncryptedSettingDto
{
    /// <summary>
    /// The plain text value to encrypt and store
    /// </summary>
    [Required]
    // 64KB：与实体 Value 列（已移除长度上限，映射 text/nvarchar(max)）对齐；
    // 特大值（如主题快照 64KB）走专用服务写入，通用 CRUD 仍保留有界上限防滥用。
    [MaxLength(65536)]
    public string Value { get; set; } = null!;

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 配置分组信息 DTO
/// </summary>
public class SettingGroupDto
{
    /// <summary>
    /// 分组名称
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 该分组下的配置数量
    /// </summary>
    public int SettingCount { get; set; }
}

/// <summary>
/// System information DTO
/// </summary>
public class SystemInfoDto
{
    /// <summary>
    /// Application name
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Framework version
    /// </summary>
    public string FrameworkVersion { get; set; } = string.Empty;

    /// <summary>
    /// .NET runtime version
    /// </summary>
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>
    /// Operating system
    /// </summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>
    /// Application start time (UTC)
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Application uptime
    /// </summary>
    public string Uptime { get; set; } = string.Empty;

    /// <summary>
    /// Environment name (Development, Staging, Production)
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Loaded module names (in load order)
    /// </summary>
    public List<SystemModuleInfoDto> LoadedModules { get; set; } = new();
}

/// <summary>
/// Module info within system info
/// </summary>
public class SystemModuleInfoDto
{
    /// <summary>
    /// Module type name (short)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module assembly name
    /// </summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>
    /// Whether the module is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Module load order
    /// </summary>
    public int LoadOrder { get; set; }
}
