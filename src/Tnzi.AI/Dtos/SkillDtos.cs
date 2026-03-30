namespace Tnzi.AI.Dtos;

/// <summary>
/// 技能摘要 DTO（列表展示）
/// </summary>
public class SkillSummaryDto
{
    /// <summary>Skill entity ID</summary>
    public Guid Id { get; set; }

    /// <summary>Skill slug (human-readable identifier)</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Skill scope</summary>
    public SkillScope Scope { get; set; }

    /// <summary>Skill name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Skill description</summary>
    public string? Description { get; set; }

    /// <summary>When to use this skill</summary>
    public string? WhenToUse { get; set; }

    /// <summary>Tags</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Priority (higher = more preferred)</summary>
    public int Priority { get; set; }

    /// <summary>Version</summary>
    public string? Version { get; set; }

    /// <summary>Author</summary>
    public string? Author { get; set; }

    /// <summary>Whether enabled</summary>
    public bool Enabled { get; set; }

    /// <summary>Skill source</summary>
    public SkillSource Source { get; set; }

    /// <summary>Category ID</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Category name (for display)</summary>
    public string? CategoryName { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 技能详情 DTO（含完整内容和参数定义）
/// </summary>
public class SkillDetailDto : SkillSummaryDto
{
    /// <summary>Prompt template content</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Parameter definitions</summary>
    public List<SkillParameter> Parameters { get; set; } = [];

    /// <summary>Allowed tool groups (constraints)</summary>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>Required model (constraint)</summary>
    public string? RequiredModel { get; set; }

    /// <summary>Required provider (constraint)</summary>
    public string? RequiredProvider { get; set; }

    /// <summary>Dependency requirements</summary>
    public SkillRequirements? Requirements { get; set; }

    /// <summary>Owner user ID (User scope only)</summary>
    public Guid? OwnerUserId { get; set; }
}

/// <summary>
/// 创建技能 DTO
/// </summary>
public class CreateSkillDto
{
    /// <summary>Skill slug (lowercase letters, digits, hyphens; max 64 chars)</summary>
    [Required]
    [MaxLength(64)]
    public string Slug { get; set; } = null!;

    /// <summary>Skill scope (Tenant or User)</summary>
    public SkillScope Scope { get; set; } = SkillScope.User;

    /// <summary>Skill name</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>Skill description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Prompt template content</summary>
    [Required]
    public string Content { get; set; } = null!;

    /// <summary>When to use this skill</summary>
    [MaxLength(2000)]
    public string? WhenToUse { get; set; }

    /// <summary>Parameter definitions</summary>
    public List<SkillParameter>? Parameters { get; set; }

    /// <summary>Allowed tool groups (constraint)</summary>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>Individual tool whitelist (constraint)</summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>Individual tool blacklist (constraint)</summary>
    public List<string>? DeniedTools { get; set; }

    /// <summary>Required model (constraint)</summary>
    [MaxLength(100)]
    public string? RequiredModel { get; set; }

    /// <summary>Required provider (constraint)</summary>
    [MaxLength(50)]
    public string? RequiredProvider { get; set; }

    /// <summary>Dependency requirements</summary>
    public SkillRequirements? Requirements { get; set; }

    /// <summary>Tags</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Priority (higher = more preferred)</summary>
    public int Priority { get; set; }

    /// <summary>Version</summary>
    [MaxLength(50)]
    public string? Version { get; set; }

    /// <summary>Author</summary>
    [MaxLength(200)]
    public string? Author { get; set; }

    /// <summary>Category ID</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Whether enabled</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 更新技能 DTO（所有字段可选）
/// </summary>
public class UpdateSkillDto
{
    /// <summary>Skill name</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Skill description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Prompt template content</summary>
    public string? Content { get; set; }

    /// <summary>When to use this skill</summary>
    [MaxLength(2000)]
    public string? WhenToUse { get; set; }

    /// <summary>Parameter definitions</summary>
    public List<SkillParameter>? Parameters { get; set; }

    /// <summary>Allowed tool groups (constraint)</summary>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>Individual tool whitelist (constraint)</summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>Individual tool blacklist (constraint)</summary>
    public List<string>? DeniedTools { get; set; }

    /// <summary>Required model (constraint)</summary>
    [MaxLength(100)]
    public string? RequiredModel { get; set; }

    /// <summary>Required provider (constraint)</summary>
    [MaxLength(50)]
    public string? RequiredProvider { get; set; }

    /// <summary>Dependency requirements</summary>
    public SkillRequirements? Requirements { get; set; }

    /// <summary>Tags</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Priority (higher = more preferred)</summary>
    public int? Priority { get; set; }

    /// <summary>Version</summary>
    [MaxLength(50)]
    public string? Version { get; set; }

    /// <summary>Author</summary>
    [MaxLength(200)]
    public string? Author { get; set; }

    /// <summary>Category ID (set to null to uncategorize)</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Whether enabled</summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// 技能激活参数 DTO
/// </summary>
public class SkillActivateDto
{
    /// <summary>Template parameters to substitute</summary>
    public Dictionary<string, string>? Parameters { get; set; }
}

/// <summary>
/// 技能使用统计 DTO
/// </summary>
public class SkillUsageStatsDto
{
    /// <summary>Total number of database skills</summary>
    public int TotalSkills { get; set; }

    /// <summary>Enabled skills count</summary>
    public int EnabledSkills { get; set; }

    /// <summary>Disabled skills count</summary>
    public int DisabledSkills { get; set; }

    /// <summary>Tenant-scoped skills count</summary>
    public int TenantScopeSkills { get; set; }

    /// <summary>User-scoped skills count</summary>
    public int UserScopeSkills { get; set; }

    /// <summary>Total activations across all skills</summary>
    public long TotalActivations { get; set; }
}

/// <summary>
/// 热门技能 DTO
/// </summary>
public class PopularSkillDto
{
    /// <summary>Skill slug</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Skill name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Skill scope</summary>
    public SkillScope Scope { get; set; }

    /// <summary>Skill source</summary>
    public SkillSource Source { get; set; }

    /// <summary>Total activation count</summary>
    public long ActivationCount { get; set; }

    /// <summary>Last activated time</summary>
    public DateTime? LastActivatedAt { get; set; }
}

/// <summary>
/// 技能激活结果
/// </summary>
public class SkillActivationResult
{
    /// <summary>Skill slug</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Skill name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Rendered prompt content (parameters substituted)</summary>
    public string RenderedContent { get; set; } = string.Empty;

    /// <summary>Allowed tool groups (from constraints)</summary>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>Required model (from constraints)</summary>
    public string? RequiredModel { get; set; }

    /// <summary>Required provider (from constraints)</summary>
    public string? RequiredProvider { get; set; }

    /// <summary>Warnings (unused params, missing optional params, etc.)</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 技能分页查询 DTO
/// </summary>
public class SkillQueryDto : PagedQueryDto
{
    /// <summary>Keyword to search in name/description</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by scope</summary>
    public SkillScope? Scope { get; set; }

    /// <summary>Filter by enabled status</summary>
    public bool? Enabled { get; set; }

    /// <summary>Filter by tag</summary>
    public string? Tag { get; set; }

    /// <summary>Filter by category ID</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Sort by field (name, priority, creationTime, activationCount). Default: creationTime</summary>
    public string? SortBy { get; set; }

    /// <summary>Sort descending (default: true)</summary>
    public bool SortDesc { get; set; } = true;
}

/// <summary>
/// 技能导出 DTO
/// </summary>
public class SkillExportDto
{
    /// <summary>Skill slug</summary>
    public string Slug { get; set; } = null!;

    /// <summary>Skill name</summary>
    public string Name { get; set; } = null!;

    /// <summary>Skill description</summary>
    public string? Description { get; set; }

    /// <summary>Prompt template content</summary>
    public string Content { get; set; } = null!;

    /// <summary>When to use</summary>
    public string? WhenToUse { get; set; }

    /// <summary>Parameter definitions</summary>
    public List<SkillParameter>? Parameters { get; set; }

    /// <summary>Allowed tool groups</summary>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>Allowed tools</summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>Denied tools</summary>
    public List<string>? DeniedTools { get; set; }

    /// <summary>Required model</summary>
    public string? RequiredModel { get; set; }

    /// <summary>Required provider</summary>
    public string? RequiredProvider { get; set; }

    /// <summary>Requirements</summary>
    public SkillRequirements? Requirements { get; set; }

    /// <summary>Tags</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Priority</summary>
    public int Priority { get; set; }

    /// <summary>Version</summary>
    public string? Version { get; set; }

    /// <summary>Author</summary>
    public string? Author { get; set; }

    /// <summary>Enabled</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 技能导入结果 DTO
/// </summary>
public class SkillImportResultDto
{
    /// <summary>Number of skills created</summary>
    public int Created { get; set; }

    /// <summary>Number of skills updated</summary>
    public int Updated { get; set; }

    /// <summary>Number of skills skipped (e.g., slug conflict with system skill)</summary>
    public int Skipped { get; set; }

    /// <summary>Error messages for failed imports</summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// 技能导入请求 DTO
/// </summary>
public class SkillImportRequestDto
{
    /// <summary>Skills to import</summary>
    [Required]
    public List<SkillExportDto> Skills { get; set; } = null!;

    /// <summary>Target scope for imported skills</summary>
    public SkillScope TargetScope { get; set; } = SkillScope.Tenant;
}
