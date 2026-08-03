namespace Tnzi.Identity.Entities;

/// <summary>
/// 用户详细信息实体
/// 与 User 表是一对一关系（通过 UserId 唯一约束）
/// </summary>
public class UserDetail : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 用户
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// 获取或设置 名字
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// 获取或设置 姓氏
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// 获取 完整姓名
    /// </summary>
    [NotMapped]
    public string? FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// 获取或设置 昵称
    /// </summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// 获取或设置 头像URL（外部链接，如 OAuth 获取的头像）
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// 获取或设置 头像文件ID（用于集成文件管理系统）。
    ///
    /// <c>Public = true</c>：头像天生就是要以匿名 <c>&lt;img src="/api/files/{id}/download"&gt;</c>
    /// 渲染的（消息气泡、成员列表、评论区都不会带 Authorization 头），因此只要有文件 id
    /// 写进这个字段，框架就在同一个事务里把该文件标记为公开可读。
    /// 声明放在字段上而不是交给调用方：写头像的路径有个人中心、管理端、OAuth 导入、
    /// 消费应用自己的服务等多条，任何一条漏传参数都会让头像变成 404。
    /// </summary>
    [FileField(Public = true)]
    public Guid? AvatarId { get; set; }

    /// <summary>
    /// 获取或设置 性别（0-未知，1-男，2-女）
    /// </summary>
    public int Gender { get; set; }

    /// <summary>
    /// 获取或设置 生日
    /// </summary>
    public DateTime? Birthday { get; set; }

    /// <summary>
    /// 获取或设置 地址
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// 获取或设置 个人简介
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// 获取或设置 个人网站
    /// </summary>
    public string? Website { get; set; }
}

