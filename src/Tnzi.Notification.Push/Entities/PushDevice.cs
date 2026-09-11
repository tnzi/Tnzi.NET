namespace Tnzi.Notification.Push.Entities;

/// <summary>
/// 一台已注册的设备：一个推送令牌，连同它当前归属的主体。按令牌唯一，一台设备一行。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>唯一键是令牌，不是主体。</b>一个用户可以有多台设备（手机 + 平板 + 浏览器），
/// 而一台设备换主体时，那个令牌必须<b>改挂过去</b>而不是多出一行 ——
/// 令牌是设备的地址，同一时刻只能属于一个主体。按 (UserId, Token) 做唯一会让前一个用户
/// 继续收到本该发给新用户的推送，且没有任何症状。
/// </para>
/// <para>
/// ★ <b>一行有两个可以并存的寻址维度</b>：<see cref="UserId"/>（这台设备上登录着谁）
/// 与 <see cref="DeviceKeyHash"/>（这台设备自己的匿名身份）。两者各自可空，但<b>不该同时为空</b>
/// —— 一行没有任何主体，既无从投递也无从清理，只会永久占着一个令牌不放。
/// </para>
/// <para>
/// ★ <b>匿名身份与主题广播的分界。</b>「连谁装了这个 App 都不该落库」的应用仍然应当选主题广播
/// （<c>IPushSender.SendToTopicAsync</c>）：那条路一个标识符都不存，这正是选它的全部理由。
/// 但主题投递刻意不在通知管线上，于是它没有投递记录、没有重试、也没有退订。
/// 既要按设备 p2p、又要这三样的匿名应用（典型形态是「设备提交表单，处理完把回执推回这台设备」），
/// 走的是本表的<b>无主行</b>：服务端签发一枚设备密钥、库里只留它的哈希，<see cref="UserId"/> 留空。
/// </para>
/// <para>
/// ★ <b>匿名行的 <c>Id</c> 是从设备密钥<b>派生</b>的，不走框架的顺序 GUID。</b>
/// 这个 Id 就是消费方写进业务记录的「设备归属键」（哪台设备提交了这份表单），
/// 所以它的生命周期必须<b>长于推送令牌</b>：令牌被网关判死时本行会被
/// <c>IPushDeviceService.RetireAsync</c> 删掉，而那些已提交的表单还等着回执。
/// 派生让客户端凭同一枚密钥重建出<b>逐字相同</b>的 Id，行回来了、回执照发。
/// 换成随机 Id 的话，退役一次就把此前所有表单的回执永久断掉，而且毫无症状：
/// 表单还在、状态正常，只是再没有推送来过。见 <c>AnonymousDeviceId</c>。
/// <para>
/// 代价是匿名行的主键随机分布，失去顺序 GUID 的插入局部性。设备表的行数与写入频率
/// 都在可接受范围内，而上面那条正确性不可交换。
/// </para>
/// </para>
/// <para>
/// ★ <b>刻意不带软删除</b>（用 <see cref="AuditedEntity{TKey}"/> 而非
/// <see cref="FullAuditedEntity{TKey}"/>）。这张表的价值在于「现在还能投递到哪些设备」，
/// 而令牌失效后留一行墓碑没有任何用途：它不是业务凭证、没有审计义务、也无从回溯。
/// 更要紧的是本模块会在 FCM 回 <c>Unregistered</c> 时自动退役令牌，软删除会让这张表
/// 只增不减，而每一行都是一个「哪台设备装了这个 App」的可关联事实。
/// </para>
/// <para>
/// ★ <b>本表可以是空的，那不是接线断了。</b>只使用主题广播（<c>SendToTopicAsync</c>）的
/// 应用一个设备标识符都不存 —— 那正是选主题投递的理由。加载本模块并不意味着必须注册设备。
/// </para>
/// </remarks>
public class PushDevice : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户 ID（未启用多租户时为 null）。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>令牌当前所在设备上登录着的用户；匿名设备为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 可空是为了让「没有账号体系的 App」也能按设备 p2p 投递，见类注释里那条分界。
    /// 有值时本行回答「发给这个人要投到哪几台设备」，由
    /// <c>IPushDeviceService.ResolveRecipientsAsync</c> 使用；为空时按
    /// <see cref="DeviceKeyHash"/> 对应的匿名身份寻址，由 <c>ResolveByDeviceIdsAsync</c> 使用。
    /// <para>
    /// ★ 两者<b>并存而不是互斥</b>：一台设备可以既登录着某个用户、又持有匿名身份
    /// （用户是在提交过匿名表单之后才注册账号的）。清掉任何一边都会让那一边的投递静默停止。
    /// </para>
    /// </remarks>
    public Guid? UserId { get; set; }

    /// <summary>匿名设备密钥的哈希；登录设备若从未匿名注册过则为 <see langword="null"/>。</summary>
    /// <remarks>
    /// ★ <b>只存哈希，明文只在签发那一次交给客户端。</b>持有密钥的那一端没有账号，
    /// 拿着这串字符本身就是全部凭据 —— 与 <c>OneTimeToken</c> 说的是同一个形状，
    /// 因此直接复用它：不加盐、不做慢哈希（要按哈希做等值查询，加盐就查不了了），
    /// 列宽取 <c>OneTimeToken.HashLength</c>。
    /// <para>
    /// ★ 密钥<b>验证不出来</b>时（哈希查不到行），必须与「过期」「不存在」回答同一句话，
    /// 否则这个端点会变成一个探针。这一条属于调用方，见 <c>PushDeviceService</c>。
    /// </para>
    /// </remarks>
    public string? DeviceKeyHash { get; set; }

    /// <summary>客户端自报的平台设备标识（iOS <c>identifierForVendor</c>、Firebase 安装 ID、
    /// Android SSAID 之类）；纯粹为了让人认出这台设备是哪台。</summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>它不参与任何寻址，一处都不参与。</b>寻址只认 <see cref="UserId"/> 与
    /// <see cref="DeviceKeyHash"/>。本模块<b>刻意不提供</b>按本列解析收件人的方法 ——
    /// 少了那个方法，误用就需要有人绕过服务层直接查实体，那是一个显式动作而不是顺手为之。
    /// </para>
    /// <para>
    /// ★★ <b>因为它是可冒名的。</b>客户端说自己是谁就是谁，服务端验不了。而它同时会作为
    /// 辨认标识流进业务表、日志、管理界面、导出和支持工单 —— 没人会把一个「原生自带、
    /// 不用特意存」的值当秘密保护。拿它当寻址键，攻击就是一次普通请求：报上别人的标识
    /// 加自己的令牌，那台设备的推送地址就改挂过来了，而两边日志都干净。
    /// <see cref="DeviceKeyHash"/> 对应的密钥则从不离开客户端的安全存储，不进业务表也不进日志。
    /// </para>
    /// <para>
    /// ★ <b>不做唯一约束</b>，三条理由各自独立成立：①它可冒名，唯一约束会让人靠抢占一个值
    /// 来阻止真实设备注册；②iOS 的 <c>identifierForVendor</c> 在同一 vendor 的多个 App 之间
    /// <b>共享同一个值</b>，于是同一个标识合法地出现在多行；③它不参与寻址，唯一性本来就没有意义。
    /// </para>
    /// </remarks>
    public string? ExternalDeviceId { get; set; }

    /// <summary>推送令牌（FCM 注册令牌）。</summary>
    /// <remarks>
    /// ★ 列宽与 <c>Notification_Recipient.Address</c> 对齐，见
    /// <see cref="Configs.PushDeviceConfiguration"/>：本列存得下、那一列存不下的令牌，
    /// 会在投递落库时把整批收件人状态一起回滚掉。
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>设备所在平台，仅用于辨认与统计，不决定投递路径。</summary>
    public DevicePlatform Platform { get; set; }

    /// <summary>设备名（可选），例如机型或浏览器名，用于让用户在设备列表里认出自己那台。</summary>
    public string? DeviceName { get; set; }

    /// <summary>最近一次客户端上报本令牌的时间。</summary>
    /// <remarks>
    /// 客户端每次启动都会拿到令牌并上报，所以这个值近似「这台设备最后一次打开 App 是什么时候」。
    /// 用途是清理：FCM 只在投递时才告诉你令牌死了，而一台再没打开过的设备可能永远等不到那次投递。
    /// </remarks>
    public DateTime LastSeenAt { get; set; }
}
