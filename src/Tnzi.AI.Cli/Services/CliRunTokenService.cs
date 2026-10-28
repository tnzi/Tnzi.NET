namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 签发与校验<b>运行范围</b>的回写凭据。
/// </summary>
/// <remarks>
/// <para>
/// 外部 agent 可能需要反过来调用平台（建工单、发评论、改状态）。框架已经有更合适的通道：
/// <c>Tnzi.AI.Mcp</c> 把 agent 与消费方自定义工具暴露为 Streamable HTTP MCP server，而每个编码 CLI 都原生支持 MCP。
/// 缺的只是<b>凭据</b>。
/// </para>
/// <para>
/// 凭据必须是运行范围的：绑到一次运行，随它结束失效；能调用的工具由部署方的
/// <c>AI:Cli:WriteBack:AllowedTools</c> 决定（MCP server 按它过滤 <c>tools/list</c>、拒绝越界的 <c>tools/call</c>），
/// 不来自 Agent 的权限，<b>更不是</b>派发这次运行的那个人的完整权限：外部 agent 能执行任意代码，
/// 把派发者的权限交给它等于把那个人的账号交出去。
/// </para>
/// <para>
/// 数据库里存的是<b>哈希</b>：数据库泄漏不该等于「拿到一把还能用的钥匙」。原文在运行期间写在工作目录的 MCP 配置里
/// （CLI 只认这种投递方式）。运行到达终态即作废：<see cref="ValidateAsync"/> 只认进行中的运行，
/// 写终态的各处（执行器的完成 / 失败 / 取消、队列的租约回收）同时清掉哈希，终态记录里不留任何凭据材料。
/// </para>
/// <para>
/// ★ 收窄（运行范围、工具白名单、租户）只对持有这把凭据的那次运行成立，<b>挡不住同宿主上的别的外部运行</b>：
/// 所有运行共用宿主进程这一个 OS 账号，任一运行都读得到别的运行工作目录里的凭据原文并以它的租户调用 MCP。
/// 多租户或互不信任的使用者须按宿主级隔离部署。
/// </para>
/// </remarks>
public class CliRunTokenService : IRunScopedCredentialValidator
{
    /// <summary>凭据前缀，便于在日志里一眼认出它是什么（前缀本身不含机密）。</summary>
    public const string TokenPrefix = "tnzi-run_";

    private readonly IRepository<CliRun, Guid> _repository;
    private readonly ILogger<CliRunTokenService> _logger;
    private readonly IOptionsMonitor<CliAgentOptions>? _options;

    /// <summary>初始化凭据服务。</summary>
    /// <param name="repository">运行记录仓储。</param>
    /// <param name="logger">日志。</param>
    /// <param name="options">模块配置；缺席时凭据不许调用任何工具（失败关闭）。</param>
    public CliRunTokenService(
        IRepository<CliRun, Guid> repository,
        ILogger<CliRunTokenService> logger,
        IOptionsMonitor<CliAgentOptions>? options = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _options = options;
    }

    /// <summary>
    /// 为一次运行签发凭据，返回<b>原文</b>（只此一次）。
    /// </summary>
    public async Task<string> IssueAsync(CliRun run, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Check.NotNull(run);

        var secret = TokenPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var hash = Hash(secret);
        var expiresAt = DateTime.UtcNow.Add(lifetime);

        // 只写这两列。整实体回写会把调用方加载时的旧值一起写回：别的副本此刻写下的取消标记被改回 false，
        // 续租写下的新租约被改回认领时的旧值。跨租户：签发发生在后台作用域，运行行带着自己的租户。
        await _repository.AcrossTenants()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.WriteBackTokenHash, hash)
                .SetProperty(r => r.WriteBackTokenExpiresAt, expiresAt), cancellationToken);

        // 让调用方手里的那份与库里一致。终态写入按列清掉这两列，不读这里的值。
        run.WriteBackTokenHash = hash;
        run.WriteBackTokenExpiresAt = expiresAt;

        return secret;
    }

    /// <inheritdoc />
    public async Task<RunScopedCredential?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(token);
        var now = DateTime.UtcNow;

        // 三个条件缺一不可：哈希匹配、未过期、运行仍在进行中。
        // 第三条是关键 —— 运行结束后凭据必须立刻失效，哪怕名义上还没到期。
        // 跨租户查：这里是未认证的 MCP 入站请求，没有当前租户；凭据本身就是范围
        // （256 位随机数的哈希），按它查到的那一行的 TenantId 才是答案，而不是过滤器的输入。
        var match = await _repository.AcrossTenants()
            .Where(r => r.WriteBackTokenHash == hash
                        && r.WriteBackTokenExpiresAt != null
                        && r.WriteBackTokenExpiresAt > now
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
            .Select(r => new { r.Id, r.AgentId, r.TenantId, r.CreatorId, r.ThreadId })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return null;
        }

        _logger.LogDebug("Accepted run-scoped credential for CLI run {RunId} (agent {AgentId})", match.Id, match.AgentId);

        // 调用面来自配置而不是运行记录：它是部署方的安全决定，不随单次派发变化。
        // 配置缺席时是空列表 —— 凭据仍然有效但一个工具都不许调（失败关闭）。
        var allowedTools = _options?.CurrentValue.WriteBack.AllowedTools
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList() ?? [];

        return new RunScopedCredential
        {
            RunId = match.Id,
            AgentId = match.AgentId,
            TenantId = match.TenantId,
            // 派发者与租户同一来源（运行记录），消费方工具据此做按用户的授权
            CreatorId = match.CreatorId,
            // 这次运行在哪段对话里：消费方工具按线程维护状态时不必再拿运行 id 回查
            ThreadId = match.ThreadId,
            AllowedToolNames = allowedTools
        };
    }

    private static string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
