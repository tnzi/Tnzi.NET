namespace Tnzi.Identity.Dtos;

/// <summary>
/// begin 阶段的产物：一份交给浏览器的 WebAuthn 选项，外加一个用来取回服务端挑战状态的句柄。
/// </summary>
/// <remarks>
/// ★ <strong>挑战状态不放在客户端。</strong>ASP.NET Core Identity 的 <c>SignInManager</c> 走 cookie
/// （<c>Identity.PasskeyAttestationState</c> / <c>Identity.PasskeyAssertionState</c>）来在两步之间传递它，
/// 而 Tnzi 是纯 Bearer 的前后端分离 API：cookie 会把这条流程绑死在同源浏览器上，
/// 移动端与跨域前端都用不了。所以框架改走底层的 <c>IPasskeyHandler</c>，
/// 状态存服务端缓存、只把一个不透明句柄给客户端。
/// </remarks>
public class PasskeyOptionsDto
{
    /// <summary>
    /// 直接喂给 <c>navigator.credentials.create()</c> / <c>.get()</c> 的 JSON（由运行时生成，框架不解析）。
    /// </summary>
    public string OptionsJson { get; set; } = string.Empty;

    /// <summary>
    /// 挑战状态句柄，complete 阶段原样回传。<strong>一次性</strong>：用过即失效。
    /// </summary>
    public string StateId { get; set; } = string.Empty;
}

/// <summary>
/// complete 阶段的入参：浏览器凭据 + begin 阶段拿到的句柄。
/// </summary>
public class PasskeyCompleteDto
{
    /// <summary>begin 阶段返回的 <see cref="PasskeyOptionsDto.StateId"/>。</summary>
    public string StateId { get; set; } = null!;

    /// <summary>
    /// 对 <c>navigator.credentials.create()</c> / <c>.get()</c> 结果做 JSON 序列化得到的字符串。
    /// </summary>
    public string CredentialJson { get; set; } = null!;

    /// <summary>
    /// 可选的凭据名称（"我的 iPhone"），只在注册时有意义，方便用户日后辨认并删除。
    /// </summary>
    public string? DeviceName { get; set; }
}

/// <summary>
/// 一枚已注册的 passkey 凭据。
/// </summary>
public class PasskeyCredentialDto
{
    /// <summary>凭据标识（base64url）。</summary>
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>用户给的名称，可能为空。</summary>
    public string? Name { get; set; }

    /// <summary>注册时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>该凭据是否已被同步到云端钥匙串（换机后仍可用）。</summary>
    public bool IsBackedUp { get; set; }
}

/// <summary>
/// 一枚刚签发的 passkey 注册令牌。
/// </summary>
/// <remarks>
/// ★ <strong>原文只在这里出现一次</strong>：库里存的是哈希，签发之后框架再也读不出来。
/// 应用负责把它递到用户手上（邮件、短信、当面），递送方式是业务决策。
/// </remarks>
public class PasskeyEnrollmentTokenDto
{
    /// <summary>令牌原文。</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>它指向的用户。</summary>
    public Guid UserId { get; set; }

    /// <summary>过期时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 断言 begin 的入参。
/// </summary>
public class PasskeyAssertionBeginDto
{
    /// <summary>
    /// 可选的用户名。给了就只允许该用户的凭据，不给则走 discoverable credential
    /// （用户在系统弹窗里自己挑账号，登录页连用户名输入框都不需要）。
    /// </summary>
    /// <remarks>
    /// ★ <strong>用户不存在时不会报错。</strong>照常返回一份选项 ——
    /// 否则这个匿名端点就成了用户名枚举预言机，而 passkey 的卖点之一正是不泄漏账号是否存在。
    /// </remarks>
    public string? UserName { get; set; }
}
