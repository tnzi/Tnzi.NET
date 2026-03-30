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
/// 更新 UserProfile 请求
/// </summary>
public class UpdateUserProfileDto
{
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? Content { get; set; }
}
