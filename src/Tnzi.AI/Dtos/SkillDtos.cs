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
