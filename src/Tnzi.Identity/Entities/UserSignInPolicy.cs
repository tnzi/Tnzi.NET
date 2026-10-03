namespace Tnzi.Identity.Entities;

/// <summary>
/// 一个账号的登录准入策略：凭据之外，还要满足什么才发令牌。目前只有一项，登录 IP 允许列表。
/// </summary>
/// <remarks>
/// <para>
/// <b>一人至多一行，没有行就是没有限制。</b>这张表刻意不为每个账号预建一行：绝大多数账号
/// 从来不会被限制登录地点，给他们各留一行全空的策略只会让「有没有限制」这个问题多一个
/// 要区分的状态（行不存在 / 行存在但全关）。守卫查不到行直接放行，成本是一次按唯一索引的
/// 未命中查询。
/// </para>
/// <para>
/// <b>为什么住在身份模块而不是消费应用的员工档案上。</b>它限制的是<b>一个账号</b>能从哪里签发令牌，
/// 与这个账号背后是员工、志愿者还是合作方无关；两个不同业务形状的消费应用各自把它写在自己的
/// 员工表上，然后各自复制了一遍同样的守卫和同样的地址匹配代码。放在账号这一侧，
/// 任何以框架账号登录的人都能被同一条守卫管住，员工档案只需要在自己的界面上链过来。
/// </para>
/// <para>
/// <b>与 <see cref="User"/> 之间有外键并级联</b>：策略没有独立于账号的意义，账号没了它也该没了。
/// 这与领域记录「刻意不建外键」的做法相反，因为这里两边都是身份模块自己的表。
/// </para>
/// </remarks>
public class UserSignInPolicy : AuditedEntity<Guid>
{
    /// <summary>
    /// <see cref="AllowedIps"/> 的上限。一行地址至多 43 个字符（IPv6 + 前缀长度），4000 字符装得下
    /// 几十条带注释的规则；再长的列表该换成网段而不是逐个枚举。列宽与请求 DTO 的校验共用这一个数。
    /// </summary>
    public const int AllowedIpsMaxLength = 4000;

    /// <summary>策略所属的账号。</summary>
    public Guid UserId { get; set; }

    /// <summary>导航到账号（配置为级联删除）。</summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// 开启后，该账号只能从 <see cref="AllowedIps"/> 里的地址签发令牌（含刷新令牌）。
    /// </summary>
    /// <remarks>
    /// 由 <c>IpAllowListLoginGuard</c> 在<b>凭据校验之后、令牌签发之前</b>执行，拒绝时对外与
    /// 「用户名或密码错误」同形。写入路径拒绝把它打开而列表为空；守卫侧对「开着但列表为空」
    /// 仍按不限制处理，那是给绕过写入路径的行（直接改库 / 导入）留的安全网：
    /// 一个空列表若被读成「谁都不许」，最先被锁在外面的就是来修它的人。
    /// </remarks>
    public bool IpAllowListEnabled { get; set; }

    /// <summary>
    /// 允许的登录地址，<b>原样保存</b>操作员输入的文本：一行一个，接受精确 IPv4 / IPv6 地址或 CIDR 段
    /// （<c>203.0.113.5</c>、<c>2001:db8::/32</c>），逗号与分号也算分隔符，<c>#</c> 开头的行是注释。
    /// </summary>
    /// <remarks>
    /// 不在写入时规范化：改写操作员的文本会让下一个打开这个框的人认不出自己写的列表，
    /// 注释和分组也会一并丢掉。解析发生在每次求值，由 <c>SignInIpAllowList</c> 负责，
    /// 写入校验与登录判定用的是同一段代码。
    /// </remarks>
    public string? AllowedIps { get; set; }
}
