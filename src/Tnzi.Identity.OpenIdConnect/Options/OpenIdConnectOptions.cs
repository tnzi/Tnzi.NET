namespace Tnzi.Identity.OpenIdConnect.Options;

/// <summary>
/// OpenID Connect 提供方配置（配置节 <c>Identity:OpenIdConnect</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>密钥是部署机密，刻意不做成运行期热设置</b>：签名密钥换了，已经签出去的 ID token 与访问令牌
/// 立刻对不上 JWKS；加密密钥换了，未用完的授权码与刷新令牌全部解不开。轮换靠「密钥环」：
/// 往环里加一把新的、把 Active 指向它，旧的留在环里（签名密钥继续出现在 JWKS 里供验签，
/// 加密密钥继续用于解密），等旧令牌全部过期再移除。
/// </para>
/// <para>
/// 生产环境没配签名或加密密钥时<b>启动失败</b>；只有 Development 环境会生成进程内的临时密钥并记 Warning
/// （重启即失效，多实例之间互不认）。
/// </para>
/// </remarks>
[ConfigSection("Identity:OpenIdConnect")]
public class OpenIdConnectOptions
{
    /// <summary>
    /// 签发方（<c>iss</c>）的绝对 URL，例如 <c>https://id.example.com/</c>。
    /// </summary>
    /// <remarks>
    /// 反向代理后面<b>应当显式配置</b>：不配时按请求推导（依赖 <c>AspNetCore:EnableForwardedHeaders</c> 把
    /// 协议、主机与路径前缀还原正确），推导错了的表现是依赖方校验 <c>iss</c> 失败、拒绝每一枚 ID token。
    /// </remarks>
    public string? Issuer { get; set; }

    /// <summary>
    /// 是否要求 HTTPS。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// TLS 在代理处终结时不要关它，而是开启转发头（代理把 <c>X-Forwarded-Proto: https</c> 传进来）。
    /// 只有本机开发与测试才设 <c>false</c>。
    /// </remarks>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// 浏览器还没有会话桥 cookie 时，authorize 把它送去的页面（消费方 SPA 的「继续授权」页）。
    /// </summary>
    /// <remarks>
    /// 绝对 URL 或以 <c>/</c> 开头的站内路径。框架在后面追加
    /// <c>?{ReturnUrlParameter}=&lt;本次 authorize 请求的绝对 URL&gt;</c>。那一页要做的是：确保用户已登录，
    /// 调 <c>POST /connect/session</c> 建立会话桥，再跳回 returnUrl（<c>@tnzi/core</c> 的
    /// <c>continueOidcAuthorization</c> 做完这三步并校验 returnUrl 的来源）。
    /// 不配置时 authorize 对没有会话的浏览器直接回 <c>login_required</c> 给依赖方。
    /// </remarks>
    public string? LoginUrl { get; set; }

    /// <summary>
    /// 需要用户同意（<see cref="OidcConsentType.Explicit"/>）而用户尚未同意时 authorize 跳去的页面。
    /// </summary>
    /// <remarks>
    /// 追加的查询参数：<c>{ReturnUrlParameter}</c>、<c>client_id</c>、<c>scope</c>。页面经
    /// <c>GET /api/oidc/consents/{clientId}</c> 取展示信息，同意时 <c>POST /api/oidc/consents</c> 后跳回 returnUrl，
    /// 拒绝时跳回 returnUrl 并附加 <c>tnzi_consent=denied</c>。不配置时显式同意的客户端得到 <c>consent_required</c>。
    /// </remarks>
    public string? ConsentUrl { get; set; }

    /// <summary>
    /// 附加在 <see cref="LoginUrl"/> / <see cref="ConsentUrl"/> 上的回跳参数名。默认 <c>returnUrl</c>。
    /// </summary>
    public string ReturnUrlParameter { get; set; } = "returnUrl";

    /// <summary>
    /// 签名密钥环：密钥标识 → PEM 编码的私钥（RSA 至少 2048 位，或 EC P-256 / P-384 / P-521）。
    /// </summary>
    /// <remarks>密钥材料必须来自机密源（环境变量、挂载的机密文件），不要写进随代码提交的 appsettings。</remarks>
    public Dictionary<string, string> SigningKeys { get; set; } = [];

    /// <summary>当前用于签名的密钥标识；为空且环里只有一把时用那一把。</summary>
    public string? ActiveSigningKeyId { get; set; }

    /// <summary>
    /// 加密密钥环：密钥标识 → Base64 编码的 256 位（32 字节）对称密钥。
    /// 用于授权码、刷新令牌等只有本服务自己读的令牌。
    /// </summary>
    public Dictionary<string, string> EncryptionKeys { get; set; } = [];

    /// <summary>当前用于加密的密钥标识；为空且环里只有一把时用那一把。</summary>
    public string? ActiveEncryptionKeyId { get; set; }

    /// <summary>
    /// 密钥文件路径（JSON，可与上面两个密钥环并用，标识不得重复）。容器里通常把它作为机密挂载进来。
    /// </summary>
    /// <remarks>
    /// 形状：<c>{ "activeSigningKeyId": "...", "signingKeys": { "id": "PKCS#8 PEM 格式的私钥" },
    /// "activeEncryptionKeyId": "...", "encryptionKeys": { "id": "base64" } }</c>。
    /// 文件缺失或格式错误时应用启动失败并指出原因。
    /// </remarks>
    public string? KeyFile { get; set; }

    /// <summary>访问令牌寿命。默认 10 分钟（撤销授权后已签出的访问令牌在此时间内仍可用，故宜短）。</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>ID token 寿命。默认 10 分钟。</summary>
    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>授权码寿命。默认 5 分钟。</summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>刷新令牌寿命。默认 14 天。</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// 依赖方发起登出（end_session）时是否同时撤销平台会话。默认 <c>false</c>：只清掉会话桥 cookie。
    /// </summary>
    /// <remarks>
    /// 打开后，用户在任何一个托管应用里点「退出」都会让他在平台本身也被登出（平台会话的刷新令牌随之失效）。
    /// 这是 OIDC 意义上的「提供方登出」，但对「一个平台、很多小应用」的形态往往过于激进，故默认关。
    /// </remarks>
    public bool EndSessionRevokesPlatformSession { get; set; }

    /// <summary>
    /// 除 <c>openid</c> / <c>profile</c> / <c>email</c> / <c>offline_access</c> 之外允许客户端申请的作用域。
    /// </summary>
    public List<string> AdditionalScopes { get; set; } = [];

    /// <summary>会话桥 cookie。</summary>
    public OidcSessionCookieOptions SessionCookie { get; set; } = new();
}

/// <summary>
/// 会话桥 cookie 配置：浏览器在 authorize 端点证明「我已登录平台」的凭据。
/// </summary>
/// <remarks>
/// 恒为 <c>HttpOnly</c>、<c>Secure</c>、<c>SameSite=Lax</c>：Lax 让它随依赖方发起的顶层 GET 跳转一起到达 authorize，
/// 又不随跨站的 POST / 子资源请求发出。内容是 Data Protection 加密的 <c>{userId, sessionId}</c>，
/// 每次使用都回查平台会话，所以登出、改密、管理员踢下线都会让它同时失效。
/// </remarks>
public class OidcSessionCookieOptions
{
    /// <summary>cookie 名。默认 <c>tnzi_oidc</c>。</summary>
    public string Name { get; set; } = "tnzi_oidc";

    /// <summary>
    /// cookie 的域。默认 <c>null</c>（只发给当前主机）。
    /// </summary>
    /// <remarks>
    /// 设成父域（如 <c>example.com</c>）时，反向代理的 <c>forward_auth</c> 才能在托管应用的子域上
    /// 拿到它去问 <c>GET /connect/session/check</c>。代价是这枚 cookie 会随父域下所有主机的请求发出。
    /// </remarks>
    public string? Domain { get; set; }

    /// <summary>
    /// cookie 的路径。默认 <c>/connect</c>（只随 OIDC 端点发出）。
    /// </summary>
    /// <remarks>
    /// 用代理网关时必须设成 <c>/</c>：浏览器只在请求路径落在 Path 之内时才带上 cookie，
    /// 而托管应用的请求路径是 <c>/</c> 开头的任意路径。
    /// </remarks>
    public string Path { get; set; } = "/connect";

    /// <summary>cookie 与其内容的寿命。默认 12 小时（平台会话先失效则以平台为准）。</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);
}
