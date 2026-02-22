namespace Tnzi.Identity.Dtos;

/// <summary>
/// 用户详情DTO
/// </summary>
public class UserDetailDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 名字
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// 姓氏
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// 完整姓名
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// 昵称
    /// </summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// 头像URL
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// 头像文件ID
    /// </summary>
    [FileField]
    public Guid? AvatarId { get; set; }

    /// <summary>
    /// 性别（0-未知，1-男，2-女）
    /// </summary>
    public int Gender { get; set; }

    /// <summary>
    /// 生日
    /// </summary>
    public DateTime? Birthday { get; set; }

    /// <summary>
    /// 地址
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// 个人简介
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// 个人网站
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 创建/更新用户详情DTO
/// </summary>
public class CreateUserDetailDto
{
    /// <summary>
    /// 名字
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// 姓氏
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// 昵称
    /// </summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// 头像URL
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// 头像文件ID
    /// </summary>
    [FileField]
    public Guid? AvatarId { get; set; }

    /// <summary>
    /// 性别（0-未知，1-男，2-女）
    /// </summary>
    public int Gender { get; set; }

    /// <summary>
    /// 生日
    /// </summary>
    public DateTime? Birthday { get; set; }

    /// <summary>
    /// 地址
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// 个人简介
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// 个人网站
    /// </summary>
    public string? Website { get; set; }
}
