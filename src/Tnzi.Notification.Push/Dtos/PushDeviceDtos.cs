using System.ComponentModel.DataAnnotations;

namespace Tnzi.Notification.Push.Dtos;

/// <summary>
/// 客户端上报一个推送令牌。
/// </summary>
/// <remarks>
/// 客户端每次启动拿到令牌就调一次，语义是 <b>upsert</b>：令牌已存在就刷新归属与
/// <c>LastSeenAt</c>，不存在才新建。要求客户端「只在第一次注册」是行不通的 ——
/// FCM 会在重装、清数据、令牌轮换时换发新令牌，而客户端无从知道服务端有没有见过它。
/// <para>
/// 登录设备与匿名设备<b>共用</b>这一个输入形状：两条路的差别在于「凭什么认定这是哪台设备」
/// （登录态 vs 设备密钥），而上报的内容完全相同。
/// </para>
/// </remarks>
public class RegisterPushDeviceDto
{
    /// <summary>推送令牌（客户端 SDK 的 <c>getToken()</c> 结果）。</summary>
    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = null!;

    /// <summary>设备所在平台。</summary>
    [Required]
    public DevicePlatform Platform { get; set; }

    /// <summary>设备名（可选），让用户在设备列表里认出自己那台。</summary>
    [MaxLength(200)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// 客户端自报的平台设备标识（可选）：iOS <c>identifierForVendor</c>、Firebase 安装 ID、
    /// Android SSAID 之类。
    /// </summary>
    /// <remarks>
    /// ★ <b>只为让人在管理界面认出这台设备是哪台，不参与任何寻址。</b>它是客户端自报的、
    /// 服务端验不了的值 —— 详见 <c>PushDevice.ExternalDeviceId</c> 上那段说明。
    /// 不填也完全正常。
    /// </remarks>
    [MaxLength(128)]
    public string? ExternalDeviceId { get; set; }
}

/// <summary>
/// 按令牌注销一台设备（登出时用）。
/// </summary>
/// <remarks>
/// 令牌走请求体而不是查询串：查询串会原样进访问日志与反向代理日志，
/// 而这个值本身就是「哪台设备装了这个 App」那条事实的载体。
/// </remarks>
public class UnregisterPushDeviceDto
{
    /// <summary>要注销的推送令牌。</summary>
    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = null!;
}

/// <summary>
/// 一台匿名设备注册或刷新之后的结果：它的 Id，以及（仅首次）设备密钥。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b><see cref="DeviceKey"/> 明文只在这一次出现</b>，库里只留哈希，之后无从取回。
/// 客户端必须立刻把它存进平台的安全存储（iOS Keychain / Android Keystore），
/// 丢了就等于丢了这台设备的身份 —— 服务端<b>没有</b>找回入口，那种入口本身就是一条
/// 绕开密钥的旁路。
/// </para>
/// <para>
/// ★ <b><see cref="DeviceId"/> 是公开的、<see cref="DeviceKey"/> 是私密的</b>，两者分工不同：
/// 前者写进业务记录当归属键（哪台设备提交了这份表单），会在服务端、管理界面和日志里流转；
/// 后者只用来证明「我就是那台设备」。知道 Id 的人改不动这一行，这正是把它们分开的理由。
/// </para>
/// </remarks>
public class AnonymousDeviceRegistrationDto
{
    /// <summary>这台设备的 Id，消费方拿它做业务记录的归属键。</summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// 设备密钥明文，<b>仅在签发那一次有值</b>；带着密钥来刷新时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// ★ 「有时有值」是刻意的，它让客户端不必自己判断这是不是首次：
    /// 手上有密钥就带上，响应里有密钥就存下来。注册与刷新因此是<b>同一个端点</b>，
    /// 客户端不需要为此写一条控制流分支。
    /// </remarks>
    public string? DeviceKey { get; set; }
}

/// <summary>
/// 一台已注册设备的展示形态。
/// </summary>
public class PushDeviceDto
{
    public Guid Id { get; set; }

    /// <summary>这台设备上登录着的用户；匿名设备为 <see langword="null"/>。</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 令牌的<b>掩码</b>形式（只保留尾部若干位），例如 <c>…a1b2c3d4</c>。
    /// </summary>
    /// <remarks>
    /// ★★ 刻意不返回完整令牌。令牌<b>是凭据</b>：本表按令牌寻址，持有它就能改写或掐掉
    /// 这台设备的推送归属（两条写入路径为此都做了归属校验）。此外一次列表查询还能导出
    /// 「谁在哪几台设备上装了这个 App」的完整清单。尾部若干位足够让人在自己的设备列表里
    /// 认出某一行、也够运维对着日志核对，注销走 <see cref="Id"/> 而不是令牌原文。
    /// </remarks>
    public string TokenMask { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>客户端自报的平台设备标识；只用于辨认，不参与寻址。</summary>
    public string? ExternalDeviceId { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// admin 端设备清单的查询条件。
/// </summary>
public class PushDeviceQueryDto : PagedQueryDto
{
    /// <summary>按用户过滤（可选）。</summary>
    public Guid? UserId { get; set; }

    /// <summary>按平台过滤（可选）。</summary>
    public DevicePlatform? Platform { get; set; }

    /// <summary>只看这个时间点之后有上报过的设备（可选）。</summary>
    public DateTime? LastSeenAfter { get; set; }

    /// <summary>
    /// 只看匿名设备（<c>true</c>）或只看有登录用户的设备（<c>false</c>）；不传则两者都看。
    /// </summary>
    /// <remarks>
    /// 用途是按维度取数（清点、导出、按人群做运维动作）。<b>界面上认出一台匿名设备不靠它</b> ——
    /// 那由列表把空用户列渲染成一个显式徽章解决，因为空白单元格与「这一页还没加载出来」
    /// 在屏幕上长得一模一样。
    /// </remarks>
    public bool? AnonymousOnly { get; set; }

    /// <summary>按客户端自报的设备标识精确过滤（可选，<b>区分大小写</b>）。</summary>
    /// <remarks>
    /// 运维手上通常只有用户报过来的那个标识（「我这台是 …」），而令牌是掩码、行 id 客户端
    /// 也拿不到。这个条件是为了让那种情形查得动，<b>不是</b>一条寻址路径。
    /// <para>
    /// ★ <b>刻意不做大小写不敏感匹配</b>，尽管框架对「字符串查询」的一般口径是要处理大小写。
    /// 那条口径针对的是人打进去的搜索词；而这一列存的是平台生成的标识符
    /// （IDFV 恒为大写 UUID、SSAID 恒为小写十六进制、Firebase 安装 ID 大小写混合但固定），
    /// 同一个值只会以一种大小写出现，运维拿到它的方式是复制粘贴而不是手抄。
    /// 而 <c>ToLower()</c> 会让这一列上那条索引失效 —— 为一个实践中不发生的输入形态，
    /// 换掉这个条件存在的唯一理由。
    /// </para>
    /// </remarks>
    public string? ExternalDeviceId { get; set; }
}

/// <summary>
/// 把一组用户展开成推送收件人的结果。
/// </summary>
/// <remarks>
/// ★★ <b>为什么不直接返回一个扁平列表。</b>给 10 个用户、其中 3 个从未注册过设备时，
/// 扁平列表只会有 7 条，而调用方拿不到任何信号说明另外 3 个人<b>没有被通知到</b> ——
/// 那 3 个人不会出现在任何一条投递记录里，报表上与「这次群发只针对 7 个人」完全一致。
/// 所以解析结果必须<b>同时</b>给出解析不出设备的那些用户，由调用方决定是当失败、
/// 是降级到别的渠道、还是本来就可以忽略。框架不替它做这个判断。
/// </remarks>
public class PushRecipientResolution
{
    /// <summary>可直接填进 <c>CreateNotificationRequest.Recipients</c> 的收件人，一台设备一条。</summary>
    public List<RecipientInput> Recipients { get; set; } = [];

    /// <summary>请求里没有任何已注册设备的用户。</summary>
    public List<Guid> UsersWithoutDevices { get; set; } = [];

    /// <summary>是否每个请求的用户都至少解析出了一台设备。</summary>
    public bool IsComplete => UsersWithoutDevices.Count == 0;
}

/// <summary>
/// 把一组<b>设备 Id</b> 展开成推送收件人的结果。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>与 <see cref="PushRecipientResolution"/> 是两个类型，不是一个类型两种用法。</b>
/// 那边的 <c>UsersWithoutDevices</c> 在按设备解析时永远为空，把两件事塞进一个形状，
/// 读的人无从判断空列表意味着「都解析到了」还是「这条路根本不填它」。
/// </para>
/// <para>
/// ★ <b>「找不到」必须报出来</b>，理由与那边逐字相同：一台设备退役后（App 卸载、
/// 令牌被网关判死）它的 Id 还留在业务记录里，而那份记录的回执<b>发不出去了</b>。
/// 只少返回几条的话，它与「这次本来就没打算发给它」完全一致。
/// </para>
/// </remarks>
public class PushDeviceResolution
{
    /// <summary>可直接填进 <c>CreateNotificationRequest.Recipients</c> 的收件人，一台设备一条。</summary>
    public List<RecipientInput> Recipients { get; set; } = [];

    /// <summary>在注册表里已经找不到的设备 Id。</summary>
    public List<Guid> DeviceIdsNotFound { get; set; } = [];

    /// <summary>是否每个请求的设备都解析到了。</summary>
    public bool IsComplete => DeviceIdsNotFound.Count == 0;
}
