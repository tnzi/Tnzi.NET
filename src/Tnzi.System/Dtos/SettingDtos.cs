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
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
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
    [MaxLength(2000)]
    public string Value { get; set; } = null!;

    public string? Description { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
}

/// <summary>
/// 更新系统配置DTO
/// </summary>
public class UpdateSettingDto
{
    [Required]
    [MaxLength(2000)]
    public string Value { get; set; } = null!;

    public string? Description { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
    public SettingValueType ValueType { get; set; }
}
