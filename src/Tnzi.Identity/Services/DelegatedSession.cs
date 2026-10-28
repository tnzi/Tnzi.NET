namespace Tnzi.Identity.Services;

/// <summary>
/// 一次委派会话的签发请求：应用代码替一个<b>身份已经确认</b>的用户开出的会话，
/// 带应用自己的声明、一个刷新也推不动的绝对到期时间、一个分组键和一个给用户看的名字。
/// </summary>
/// <remarks>
/// <para>
/// 典型形态：用户在已登录的设置页里生成一枚限时口令，便携客户端在别人的电脑上拿它换一条会话。
/// 这条会话<b>就是</b>那个用户（对话、用量都记在他名下），但应用要能在每个请求上认出它、
/// 只放行少数几件事，并且在口令到期或用户撤销时让它在所有地方同时失效。
/// 交给 <see cref="IAuthService.IssueDelegatedSessionAsync"/> 签发。
/// </para>
/// <para>
/// ★ 框架<b>不</b>限制这条会话能调什么：它是一条完整的、以该用户身份行事的会话，
/// 限制由应用自己的请求闸门按 <see cref="Claims"/> 做，并且应当是<b>默认拒绝</b>的 ——
/// 框架的个人中心端点（改密码、管理会话、登记 passkey、二次验证设置）对它一样敞开。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "委派会话刚落地，声明的约束与分组撤销的形态可能调整")]
public sealed record DelegatedSessionRequest
{
    /// <summary>
    /// 分组键（必填，不超过 <see cref="DelegatedSessionRules.MaxGroupKeyLength"/> 个字符，按序数比较）。
    /// 同一用户下分组键相同的会话可以经 <see cref="ISessionRevocationService.RevokeSessionGroupAsync"/>
    /// 一次撤销。例：<c>pass:{passId:N}:{deviceId:N}</c>。
    /// </summary>
    public required string GroupKey { get; init; }

    /// <summary>
    /// 绝对到期时间（UTC；<see cref="DateTimeKind.Local"/> 会先换算，<see cref="DateTimeKind.Unspecified"/> 按 UTC 读）。
    /// 必须晚于现在。刷新令牌、access token 与会话本身都不会活过它；部署配置的
    /// <c>Identity:Session:AbsoluteLifetimeHours</c> 更早时以那个为准。
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// 在用户自己的会话列表里显示的名字（可选，不超过 <see cref="DelegatedSessionRules.MaxLabelLength"/> 个字符）。
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// 写进每一枚 access token（含刷新之后签发的）的应用声明。名字入站后原样不变，
    /// 规则见 <see cref="DelegatedSessionRules.IsAllowedClaimType"/>；至多
    /// <see cref="DelegatedSessionRules.MaxClaims"/> 条，值不超过 <see cref="DelegatedSessionRules.MaxClaimValueLength"/> 个字符。
    /// </summary>
    public IReadOnlyDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// 委派会话落在会话记录上的那部分事实。签发时写入，刷新时读回 —— 刷新出来的令牌带同一组声明、
/// 受同一个绝对到期时间约束，靠的就是这一份。
/// </summary>
/// <param name="GroupKey">分组键</param>
/// <param name="Label">显示名</param>
/// <param name="Claims">应用声明</param>
/// <param name="AbsoluteExpiresAt">
/// 会话的绝对到期时间（UTC）。写入时是请求的到期时间；会话服务读回的是落库值，
/// 即请求值与部署配置的绝对上限里更早的那个。
/// </param>
public sealed record DelegatedSessionInfo(
    string GroupKey,
    string? Label,
    IReadOnlyDictionary<string, string> Claims,
    DateTime AbsoluteExpiresAt);

/// <summary>
/// 建立一条委派会话所需的字段，交给 <see cref="ISessionService.CreateDelegatedSessionAsync"/>。
/// </summary>
/// <param name="UserId">会话主人</param>
/// <param name="DeviceInfo">设备描述（由 User-Agent 解析，可能为 null）</param>
/// <param name="IpAddress">来源地址</param>
/// <param name="UserAgent">User-Agent，会话按它绑定，与其它会话同一规则</param>
/// <param name="ExpiresAt">滑动硬过期的初值（已夹在 <see cref="DelegatedSessionInfo.AbsoluteExpiresAt"/> 之内）</param>
/// <param name="Delegation">要落到会话记录上的委派事实</param>
public sealed record DelegatedSessionCreation(
    Guid UserId,
    string? DeviceInfo,
    string? IpAddress,
    string? UserAgent,
    DateTime ExpiresAt,
    DelegatedSessionInfo Delegation);

/// <summary>
/// 委派会话的约束与声明的存取规则。签发入口、两个会话后端与测试共用这一份。
/// </summary>
[ExperimentalApi(Reason = "委派会话刚落地，声明的约束与分组撤销的形态可能调整")]
public static class DelegatedSessionRules
{
    /// <summary>分组键的最大长度。</summary>
    public const int MaxGroupKeyLength = 128;

    /// <summary>显示名的最大长度。</summary>
    public const int MaxLabelLength = 128;

    /// <summary>一条会话至多携带的应用声明数。</summary>
    public const int MaxClaims = 16;

    /// <summary>声明名的最大长度。</summary>
    public const int MaxClaimTypeLength = 64;

    /// <summary>声明值的最大长度。</summary>
    public const int MaxClaimValueLength = 256;

    /// <summary>
    /// JWT 注册声明名（<c>iss</c> / <c>exp</c> / <c>sub</c> / <c>email</c> 等）。令牌处理器自己写其中几个，
    /// 其余在入站时有既定语义；应用声明撞上任何一个，读到的都不再是应用写进去的那个意思。
    /// </summary>
    private static readonly HashSet<string> RegisteredClaimNames = typeof(JwtRegisteredClaimNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => ((string)field.GetRawConstantValue()!).ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 校验一份签发请求。通过时返回 <c>null</c>，否则返回一句说给调用方开发者看的英文原因。
    /// </summary>
    /// <param name="request">签发请求</param>
    /// <param name="utcNow">当前时刻（UTC）</param>
    public static string? Validate(DelegatedSessionRequest request, DateTime utcNow)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.GroupKey))
        {
            return "A delegated session needs a group key.";
        }

        if (request.GroupKey.Length > MaxGroupKeyLength)
        {
            return $"The group key is longer than {MaxGroupKeyLength} characters.";
        }

        if (request.Label is { Length: > MaxLabelLength })
        {
            return $"The label is longer than {MaxLabelLength} characters.";
        }

        if (ToUtc(request.ExpiresAt) <= utcNow)
        {
            return "The expiry of a delegated session must lie in the future.";
        }

        if (request.Claims is null)
        {
            return "Claims must not be null; pass an empty dictionary for none.";
        }

        if (request.Claims.Count > MaxClaims)
        {
            return $"A delegated session carries at most {MaxClaims} claims.";
        }

        foreach (var (type, value) in request.Claims)
        {
            if (!IsAllowedClaimType(type))
            {
                return $"'{type}' cannot be used as a claim name: use 1-{MaxClaimTypeLength} ASCII letters, digits, '_', '.' or '-', "
                    + "starting with a letter, and avoid registered JWT claim names, names the framework issues itself, "
                    + "and names that inbound claim mapping would rename.";
            }

            if (value is null)
            {
                return $"The claim '{type}' has no value.";
            }

            if (value.Length > MaxClaimValueLength)
            {
                return $"The value of the claim '{type}' is longer than {MaxClaimValueLength} characters.";
            }
        }

        return null;
    }

    /// <summary>
    /// 一个名字能不能当委派会话的声明名：入站之后它必须<b>原样</b>出现在
    /// <c>HttpContext.User</c> 与 SignalR 的 <c>Context.User</c> 上，而且不能冒充框架自己的声明。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三类名字一律拒绝，比较时不分大小写：
    /// ① 框架自己签发的（身份、角色、租户、<c>session_id</c>）—— 否则能伪造身份或把令牌挂到别人的会话上；
    /// ② JWT 注册声明名 —— 令牌处理器自己写 <c>exp</c> / <c>iss</c> 这几个，其余的在入站时有既定语义；
    /// ③ 入站映射表 <see cref="JwtSecurityTokenHandler.DefaultInboundClaimTypeMap"/> 里的短名 —— JWT Bearer 开着
    /// <c>MapInboundClaims</c>，这些名字进来就被改写成长 URI，应用按原名读永远读不到。
    /// 第三类按运行时的映射表现查，.NET 将来新增映射时这里跟着收紧，而不是放进一个读不回来的名字。
    /// </para>
    /// <para>
    /// 字符集限定为 ASCII 字母、数字、<c>_</c>、<c>.</c>、<c>-</c>，且以字母开头：
    /// 冒号与斜杠会让它看起来像出站映射认识的长 URI，空白与控制字符没有任何正当用途。
    /// 应用最好给自己的声明加一个前缀（例如 <c>myapp_pass</c>），免得与框架将来新增的声明撞名。
    /// </para>
    /// </remarks>
    /// <param name="claimType">声明名</param>
    public static bool IsAllowedClaimType(string? claimType)
    {
        if (string.IsNullOrEmpty(claimType) || claimType.Length > MaxClaimTypeLength || !char.IsAsciiLetter(claimType[0]))
        {
            return false;
        }

        foreach (var c in claimType)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.' && c != '-')
            {
                return false;
            }
        }

        var lower = claimType.ToLowerInvariant();
        if (JwtTokenService.IsReservedClaimType(claimType)
            || JwtTokenService.IsReservedClaimType(lower)
            || RegisteredClaimNames.Contains(lower))
        {
            return false;
        }

        return !JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Keys
            .Any(mapped => string.Equals(mapped, claimType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 把时间读成 UTC：<see cref="DateTimeKind.Local"/> 换算，<see cref="DateTimeKind.Unspecified"/> 只打 UTC 标签。
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>把声明序列化成会话记录里那一列的形态（JSON 对象）。</summary>
    public static string SerializeClaims(IReadOnlyDictionary<string, string> claims)
    {
        Check.NotNull(claims);
        return JsonSerializer.Serialize(claims);
    }

    /// <summary>
    /// 把会话记录里那一列读回声明。空值读成空集合。
    /// </summary>
    /// <remarks>
    /// ★ 读不出来时<b>抛异常，不返回空集合</b>：应用正是按这些声明把委派会话与普通会话区分开的，
    /// 一份读丢了声明的令牌在应用那里就是一条不受任何限制的会话。宁可让这次刷新失败。
    /// </remarks>
    /// <exception cref="InvalidOperationException">存储的内容不是一个字符串到字符串的 JSON 对象</exception>
    public static IReadOnlyDictionary<string, string> DeserializeClaims(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? throw new InvalidOperationException("The stored claims of a delegated session are null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The stored claims of a delegated session could not be read; refusing to issue a token without them.", ex);
        }
    }

    /// <summary>
    /// 会话服务读回来的委派事实与请求是否一致（分组键与声明逐条相同）。
    /// </summary>
    internal static bool Matches(DelegatedSessionInfo stored, DelegatedSessionRequest request)
    {
        if (!string.Equals(stored.GroupKey, request.GroupKey, StringComparison.Ordinal)
            || stored.Claims.Count != request.Claims.Count)
        {
            return false;
        }

        foreach (var (type, value) in request.Claims)
        {
            if (!stored.Claims.TryGetValue(type, out var storedValue) || !string.Equals(storedValue, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
