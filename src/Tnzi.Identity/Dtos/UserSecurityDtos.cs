namespace Tnzi.Identity.Dtos;

/// <summary>
/// 一个账号的登录准入策略，管理端读取用。
/// </summary>
public class UserSignInPolicyDto
{
    public Guid UserId { get; set; }

    /// <summary>登录 IP 允许列表是否开启。没有策略行的账号恒为 false。</summary>
    public bool IpAllowListEnabled { get; set; }

    /// <summary>允许列表原文（操作员上次保存的文本，含注释与顺序）。</summary>
    public string? AllowedIps { get; set; }

    /// <summary>从原文解析出的有效条目（去掉空行与注释）。</summary>
    public List<string> Entries { get; set; } = [];

    /// <summary>
    /// 账号因持有 <c>Identity:AccountSecurity:IpAllowListExemptRoles</c> 里的角色而不受列表约束时，
    /// 命中的那些角色名；否则为空。界面据此把「已开启」旁边的说明改成「对该账号不生效」，
    /// 而不是让操作员以为一条不会执行的限制正在保护什么。
    /// </summary>
    public List<string> ExemptedByRoles { get; set; } = [];

    /// <summary>
    /// 发出本次请求的操作员自己的地址，给「把我的地址加进去」这个按钮用。
    /// 管理员多半就坐在要放行的那间办公室里，让他去别处查自己的出口 IP 是没必要的。
    /// </summary>
    public string? CallerIpAddress { get; set; }
}

/// <summary>
/// 改写一个账号的登录 IP 允许列表。
/// </summary>
public class SetIpAllowListDto
{
    /// <summary>开启后账号只能从列表内地址签发令牌。开启时列表至少要有一个有效条目。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 允许列表文本：一行一个精确地址或 CIDR 段，逗号 / 分号也算分隔符，<c>#</c> 开头是注释。
    /// 原样保存；任何一个条目格式不对整次写入被拒绝。
    /// </summary>
    [MaxLength(UserSignInPolicy.AllowedIpsMaxLength)]
    public string? AllowedIps { get; set; }
}
