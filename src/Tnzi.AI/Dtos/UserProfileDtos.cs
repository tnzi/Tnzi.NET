namespace Tnzi.AI.Dtos;

/// <summary>
/// UserProfile 输出 DTO
/// </summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public string? PreferredLanguage { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 更新 UserProfile 请求（长度上限与列一致，超长在模型验证就答 400，而不是落库时撞列长变成 500）
/// </summary>
public class UpdateUserProfileDto
{
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Role { get; set; }

    [MaxLength(20)]
    public string? PreferredLanguage { get; set; }

    [MaxLength(16000)]
    public string? Content { get; set; }
}
