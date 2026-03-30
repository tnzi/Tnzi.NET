namespace Tnzi.AI.Dtos;

/// <summary>
/// 技能分类 DTO（支持树形结构展示）
/// </summary>
public class SkillCategoryDto
{
    /// <summary>Category ID</summary>
    public Guid Id { get; set; }

    /// <summary>Category name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly slug</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Category description</summary>
    public string? Description { get; set; }

    /// <summary>Parent category ID</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Sort order</summary>
    public int SortOrder { get; set; }

    /// <summary>Icon identifier</summary>
    public string? Icon { get; set; }

    /// <summary>Number of skills in this category</summary>
    public int SkillCount { get; set; }

    /// <summary>Child categories (populated in tree queries)</summary>
    public List<SkillCategoryDto>? Children { get; set; }
}

/// <summary>
/// 创建技能分类 DTO
/// </summary>
public class CreateSkillCategoryDto
{
    /// <summary>Category name</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>URL-friendly slug (auto-generated from name if not provided)</summary>
    [MaxLength(100)]
    public string? Slug { get; set; }

    /// <summary>Category description</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Parent category ID (null for root category)</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Sort order (lower = higher priority)</summary>
    public int? SortOrder { get; set; }

    /// <summary>Icon identifier</summary>
    [MaxLength(200)]
    public string? Icon { get; set; }
}

/// <summary>
/// 更新技能分类 DTO（所有字段可选）
/// </summary>
public class UpdateSkillCategoryDto
{
    /// <summary>Category name</summary>
    [MaxLength(100)]
    public string? Name { get; set; }

    /// <summary>Category description</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Sort order</summary>
    public int? SortOrder { get; set; }

    /// <summary>Icon identifier</summary>
    [MaxLength(200)]
    public string? Icon { get; set; }
}
