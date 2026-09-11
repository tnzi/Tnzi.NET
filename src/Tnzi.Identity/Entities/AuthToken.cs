namespace Tnzi.Identity.Entities;

/// <summary>
/// 认证令牌实体（用于业务Token管理）。
/// <para>
/// 刻意<b>不使用软删</b>（<see cref="AuditedEntity{TKey}"/> 而非 <see cref="FullAuditedEntity{TKey}"/>）：
/// 令牌是短暂凭证（刷新令牌、2FA 临时令牌），过期/轮换/登出即应物理消失，无审计留痕需求
/// （<see cref="Value"/> 已 [AuditIgnore]）。软删会让"逻辑已删"的行仍物理占用唯一索引
/// <c>(UserId, LoginProvider, Name, SessionId)</c>，而 <c>SaveTokenAsync</c> 的存在性查询走软删
/// 过滤器看不到它 → 再次插入同一把 key 直接撞唯一约束（尤其 2FA 临时令牌 key 恒为
/// <c>(user, TwoFactor, TempToken, Guid.Empty)</c>，过期被后台清扫软删后每次登录必冲突）。
/// 改硬删后不存在幽灵行，查询侧与约束侧口径一致。
/// </para>
/// </summary>
public class AuthToken : AuditedEntity<Guid>
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
    /// 获取或设置 所属登录会话ID（把令牌绑定到具体的登录设备/会话）。
    /// <see cref="Guid.Empty"/> 表示不与任何会话绑定（如 2FA 临时令牌、历史遗留刷新令牌）。
    /// 刷新令牌绑定会话后，撤销会话即令该设备的刷新令牌失效；且同一用户可为多个会话
    /// 各持有一条刷新令牌（唯一索引含 SessionId），而非旧的"每用户一行、后登录覆盖"。
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// 获取或设置 登录提供者（如：JWT, OAuth2等）
    /// </summary>
    public string LoginProvider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 令牌名称（如：AccessToken, RefreshToken）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 令牌值的<b>密文</b>（<c>IDataProtectionProvider</c>，purpose 见
    /// <see cref="ValueProtectorPurpose"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>这一列曾经存明文。</strong>它承载刷新令牌、2FA 临时令牌、
    /// 设置密码令牌 —— 每一样都是「拿着它就等于是这个人」的可用凭据，
    /// 而且刷新令牌绕过密码与两步验证。任何能读到这张表的人（一份备份、一个只读副本、
    /// 别处的一次注入、一个有 SELECT 权限的支持工程师）都能直接接管账号。
    /// 同一张表里 <see cref="PreviousValueHash"/> 早就只存哈希，
    /// 而 passkey 注册令牌、邀请令牌、step-up 记录也都只存哈希 —— 只有活着的那个值是明文。
    /// </para>
    /// <para>
    /// ★ <strong>为什么是密文而不是哈希。</strong>哈希不可逆，而这一列有两个必须<b>读回原值</b>
    /// 的消费方：刷新令牌轮换的宽限窗要把<b>当代明文</b>交给并发刷新的那一方
    /// （<c>AuthService.HandleRotatedRefreshTokenAsync</c>），以及邀请预填资料
    /// （<c>InvitationService.ReadProfileAsync</c> 把整段 JSON 读回来 —— 这一列并不只放凭据）。
    /// 等值查找由 <see cref="ValueHash"/> 承担，两者分工。
    /// </para>
    /// <para>
    /// ⚠ <strong>随之而来的运维前提：DataProtection 的 key ring 必须持久化且多实例共享。</strong>
    /// 默认按机器存放，多实例部署会互相解不开；key ring 丢失等于所有刷新令牌作废（用户重登一次）。
    /// </para>
    /// <para>
    /// [AuditIgnore] 保留：密文同样不进审计表。判据与 <c>Provider.ApiKeyEncrypted</c> 一致 ——
    /// 审计查看者与 key ring 的宿主可能是不同信任级，密文外泄面越小越好。
    /// </para>
    /// </remarks>
    [AuditIgnore]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="ValueProtectorPurpose"/> 的单一真值源。改它等于让全部存量令牌解不开。
    /// </summary>
    public const string ValueProtectorPurpose = "Tnzi.Identity.AuthToken.Value";

    /// <summary>
    /// 获取或设置 令牌值的 SHA-256（<see cref="OneTimeToken.Hash"/>，小写十六进制）——
    /// <b>按值查找走这一列</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 密文没法做等值查询（<c>Protect</c> 每次输出不同），所以查找另立一列。
    /// 哈希是确定性的，可以建索引，而它不可逆 —— 库里读到它也换不出一枚能用的令牌。
    /// 不加盐、不慢哈希的理由与 <see cref="OneTimeToken"/> 一致：令牌是密码学随机数，
    /// 没有字典可查，而加盐就没法按值查了。
    /// </para>
    /// <para>
    /// ⚠ 存量行为 <c>null</c>（明文时代没有这一列），因此升级后它们一律查不到 ——
    /// 也就是全体用户重新登录一次。这是刻意的：把明文值迁成哈希需要先读明文，
    /// 而那正是要消除的东西。
    /// </para>
    /// </remarks>
    [AuditIgnore]
    public string? ValueHash { get; set; }

    /// <summary>
    /// 获取或设置 <b>上一代</b>令牌值的 SHA-256（<see cref="OneTimeToken.Hash"/>，小写十六进制）。
    /// 仅刷新令牌轮换时写入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>这一列是刷新令牌轮换能不能算数的分水岭。</b>轮换本身（每次刷新换一个新值）
    /// 只是把令牌变短命，并不能发现盗用：旧值被直接覆盖之后，攻击者拿着被盗令牌来刷新，
    /// 得到的回答与「令牌过期了」一模一样 —— 服务端根本区分不出这是重放还是自然失效，
    /// 于是真用户被顶掉、重新登录一次，攻击者那条会话原样继续活着，全程没有任何信号。
    /// 留下上一代的哈希，才让 RFC 9700 §4.14 说的那半件事（重放检测）成立。
    /// </para>
    /// <para>
    /// ★ 存哈希不存明文：这一列的唯一用途是等值查找，不需要还原原值，
    /// 而多留一份可用凭证的明文没有任何好处。与 <see cref="OneTimeToken"/> 同口径
    /// （随机数无字典可查，故刻意不加盐、不慢哈希 —— 加盐就没法按值查了）。
    /// </para>
    /// </remarks>
    [AuditIgnore]
    public string? PreviousValueHash { get; set; }

    /// <summary>
    /// 获取或设置 上一次轮换的时刻。与 <see cref="PreviousValueHash"/> 同时写入。
    /// </summary>
    /// <remarks>
    /// 用来区分<b>并发刷新</b>与<b>重放</b>：多标签页 SPA、请求重试、移动端重连都会让
    /// 同一枚旧令牌在极短时间内被交换两次，这是正常使用而不是攻击。轮换后
    /// <c>Identity:Jwt:RefreshTokenRotationOverlapSeconds</c> 秒内再次出现上一代令牌，
    /// 按并发处理（返回当前这一代）；超出窗口才判定为重放并撤销整条会话。
    /// 没有这个窗口，用户开两个标签页就会被当成攻击者踢掉。
    /// </remarks>
    public DateTime? RotatedAt { get; set; }

    /// <summary>
    /// 获取或设置 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置 是否已使用
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// 获取或设置 使用时间
    /// </summary>
    public DateTime? UsedAt { get; set; }
}

