namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 一次凭据请求：框架连接（或重连）某个用户专属 MCP 服务器、或在调用被 401/403 拒绝后重试之前，向消费方要凭据。
/// </summary>
/// <param name="Refresh">
/// 为 <c>true</c> 表示框架刚拿着上一次给的凭据被服务器拒绝：消费方应当刷新（例如用 refresh token 换新的 access token）
/// 并返回新值；为 <c>false</c> 表示一次普通连接，返回当前有效的凭据即可。
/// </param>
[ExperimentalApi(Reason = "The per-user MCP server contract is new and may change with the first consumers")]
public readonly record struct McpCredentialRequest(bool Refresh);

/// <summary>
/// 消费方交给框架的一次性凭据。框架只把它写进这条连接的请求头，<b>不保存、不缓存、不记日志</b>，
/// 下一次连接或重连时再向消费方要。
/// </summary>
[ExperimentalApi(Reason = "The per-user MCP server contract is new and may change with the first consumers")]
public sealed class McpCredential
{
    /// <summary>访问令牌。不得包含换行或控制字符。</summary>
    public required string AccessToken { get; init; }

    /// <summary>Authorization 头的方案，默认 <c>Bearer</c>。</summary>
    public string Scheme { get; init; } = "Bearer";

    /// <summary>
    /// 随令牌一起发送的附加请求头（例如某些服务要求的 API 版本头）。
    /// 不得包含 <c>Authorization</c>：那一项只能由 <see cref="AccessToken"/> 生成。
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalHeaders { get; init; }
}

/// <summary>
/// 消费方为一个用户的某次运行贡献的远程 MCP 服务器。凭据来自回调，配置对象里不放令牌。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="McpServerConfig"/> 的区别：只允许 HTTP 系 transport（不允许 stdio）；端点按用户输入对待，
/// 每条 TCP 连接都过 egress 检查（除非 <c>AI:Mcp:AllowPrivateEndpoints</c>）；连接缓存、工具缓存、失败冷却都按
/// <see cref="UserId"/> 分区。<see cref="UserId"/> 只由框架在取服务器清单时盖上（取自请求的用户），
/// 消费方无法设置，所以一个用户的清单里夹带别人的 id 也没有用。
/// </para>
/// <para>
/// 默认 <see cref="McpServerConfig.PrefixToolNameWithServer"/> 为 <c>true</c>：用户自行添加的服务器不能靠同名工具
/// 顶掉别的来源的工具，名字是 <c>{server}__{tool}</c>（<see cref="McpToolNames"/>）。
/// 默认 <see cref="McpServerConfig.ApprovalMode"/> 沿用 <see cref="McpApprovalMode.AlwaysRequire"/>：无人值守的路径
/// （外部 CLI 经 MCP 回写通道转发）问不了人，所以会拒绝，消费方对已经由用户明确授权的服务器要显式设为
/// <see cref="McpApprovalMode.NeverRequire"/> 或在 <see cref="McpServerConfig.NeverRequireApprovalTools"/> 里点名只读工具。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "The per-user MCP server contract is new and may change with the first consumers")]
public sealed class UserMcpServerConfig : McpServerConfig
{
    /// <summary>取凭据的回调。必填；返回 <c>null</c> 表示这个用户当前没有可用凭据（连接失败，按失败冷却处理）。</summary>
    public required Func<McpCredentialRequest, CancellationToken, ValueTask<McpCredential?>> CredentialProvider { get; init; }

    /// <summary>这条配置属于哪个用户。由框架盖上，见类说明。</summary>
    public Guid UserId { get; private set; }

    public UserMcpServerConfig()
    {
        ConnectionType = McpConnectionType.Http;
        PrefixToolNameWithServer = true;
        EnforceEgressGuard = true;
    }

    /// <summary>
    /// 校验并产出盖了用户的副本；不合规抛 <see cref="ArgumentException"/>。校验的是安全边界，不是风格：
    /// 名字进入缓存分区键与工具名，端点必须是 HTTP(S)，传输必须是 HTTP，不得携带本机进程或 OAuth client_credentials 配置。
    /// </summary>
    internal UserMcpServerConfig ForUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user-scoped MCP server needs a user id.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Any(char.IsControl))
        {
            throw new ArgumentException("A user-scoped MCP server needs a name without control characters.", nameof(Name));
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"User-scoped MCP server '{Name}' needs an absolute HTTP(S) endpoint.", nameof(Endpoint));
        }

        if (ConnectionType != McpConnectionType.Http || !string.IsNullOrWhiteSpace(Command) || Arguments.Count > 0 || Environment.Count > 0)
        {
            throw new ArgumentException($"User-scoped MCP server '{Name}' must use HTTP transport; local processes are not allowed.", nameof(ConnectionType));
        }

        if (OAuth is not null)
        {
            throw new ArgumentException($"User-scoped MCP server '{Name}' takes its credential from CredentialProvider, not OAuth client_credentials.", nameof(OAuth));
        }

        if (Headers.Keys.Any(k => string.Equals(k, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"User-scoped MCP server '{Name}' must not set an Authorization header; return it from CredentialProvider.", nameof(Headers));
        }

        var copy = (UserMcpServerConfig)MemberwiseClone();
        copy.UserId = userId;
        // 不依赖消费方有没有记得设：这几项是隔离边界的一部分
        copy.TenantKey = null;
        copy.EnforceEgressGuard = true;
        return copy;
    }
}

/// <summary>
/// 消费方实现：返回一个用户当前连接着的远程 MCP 服务器。在每次取工具（每次 agent 运行、每次经 MCP 回写通道的
/// <c>tools/list</c>）时被调用，所以实现应当自带缓存；增删服务器后需要立刻生效的，再调 <see cref="IUserMcpConnections"/>。
/// </summary>
[ExperimentalApi(Reason = "The per-user MCP server contract is new and may change with the first consumers")]
public interface IUserMcpServerSource
{
    /// <summary>
    /// 取用户的服务器清单。清单为空 = 这个用户没有连接任何服务器。抛异常时框架记警告并按空清单处理（失败关闭）。
    /// 同一清单内重名的服务器只保留第一个。
    /// </summary>
    Task<IReadOnlyList<UserMcpServerConfig>> GetServersAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// 消费方在用户断开账号、撤销凭据、改了端点之后调用：丢掉该用户对应的连接与工具缓存。
/// 之后的运行不会再看到已从 <see cref="IUserMcpServerSource"/> 清单里移除的服务器的工具。
/// </summary>
[ExperimentalApi(Reason = "The per-user MCP server contract is new and may change with the first consumers")]
public interface IUserMcpConnections
{
    /// <summary>断开并忘掉一个用户的一台服务器（连接、工具缓存、失败冷却）。幂等。</summary>
    Task DisconnectAsync(Guid userId, string serverName, CancellationToken ct = default);

    /// <summary>断开并忘掉一个用户的全部服务器。幂等。</summary>
    Task DisconnectAllAsync(Guid userId, CancellationToken ct = default);
}
