namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 签发与校验<b>运行范围</b>的回写凭据。
/// </summary>
/// <remarks>
/// <para>
/// 外部 agent 可能需要反过来调用平台（建工单、发评论、改状态）。框架已经有更合适的通道：
/// <c>Tnzi.AI.Mcp</c> 把 agent 暴露为 HTTP/SSE MCP server，而每个编码 CLI 都原生支持 MCP。
/// 缺的只是<b>凭据</b>。
/// </para>
/// <para>
/// 凭据必须是运行范围的：绑到一次运行，随它结束失效，权限上限是该 Agent 自身的权限，
/// <b>绝不是</b>派发这次运行的那个人的完整权限 —— 外部 agent 能执行任意代码，
/// 把派发者的权限交给它等于把那个人的账号交出去。
/// </para>
/// <para>
/// 存的是<b>哈希</b>，原文只在启动子进程的那一刻存在。数据库泄漏不该等于「拿到一把还能用的钥匙」。
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

        run.WriteBackTokenHash = Hash(secret);
        run.WriteBackTokenExpiresAt = DateTime.UtcNow.Add(lifetime);

        await _repository.UpdateAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

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
            .Select(r => new { r.Id, r.AgentId, r.TenantId })
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
            AllowedToolNames = allowedTools
        };
    }

    /// <summary>
    /// 立刻作废一次运行的凭据。
    /// </summary>
    /// <remarks>
    /// 运行到达终态时调用。虽然 <see cref="ValidateAsync"/> 已经按状态挡掉了，
    /// 但把哈希也清掉意味着终态记录里根本不留任何凭据材料 —— 少一份可被离线爆破的东西。
    /// </remarks>
    public async Task RevokeAsync(Guid runId, CancellationToken cancellationToken)
    {
        await _repository.AsQueryable()
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.WriteBackTokenHash, (string?)null)
                .SetProperty(r => r.WriteBackTokenExpiresAt, (DateTime?)null), cancellationToken);
    }

    private static string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
