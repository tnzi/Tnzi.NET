namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 远程执行目标（每用户一个的工作区容器、一台工作机）上的 CLI 登记：一个目标上的一个 CLI 一行运行时，带版本与可执行文件路径。
/// </summary>
/// <remarks>
/// <para>
/// 使用方在目标的代理连上服务端时调用 <see cref="RegisterAsync"/>（报告目标上装了哪些 CLI、各是什么版本），断开时调用
/// <see cref="MarkOfflineAsync"/>。在线与否此后由服务端副本维持：服务这个目标的副本（<see cref="ICliHost.GetCoverage"/>）每轮刷新它的
/// <c>LastSeenAt</c>，忘了调 <c>MarkOfflineAsync</c> 也只会让它在 <c>AI:Cli:Activation:OnlineTtl</c> 之后被判离线。
/// </para>
/// <para>
/// 运行时是宿主级资源，不分租户（与本机探测登记的运行时相同）。目标标识不能与服务端机器名相同：那是本机运行时的 <c>HostId</c>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Per-workspace runtimes are new")]
public interface ICliRemoteRuntimeRegistry
{
    /// <summary>
    /// 登记（或刷新）一个执行目标上的 CLI，并把它标成在线。目标上已登记、这次没报告的 CLI 标成离线。
    /// </summary>
    /// <exception cref="ArgumentException">目标标识为空、过长、与服务端机器名或一个本机运行时相同。</exception>
    Task<CliTargetRegistrationResult> RegisterAsync(CliTargetRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>把一个执行目标上的运行时全部标成离线（代理断开时）。返回改动的行数。</summary>
    Task<int> MarkOfflineAsync(string targetId, CancellationToken cancellationToken = default);
}

/// <summary>一次登记：一个执行目标与它上面的 CLI。</summary>
public sealed record CliTargetRegistration
{
    /// <summary>执行目标标识（≤200 字符，例如 <c>ws-{userId}</c>）；运行时的 <c>HostId</c>。</summary>
    public required string TargetId { get; init; }

    /// <summary>展示名的后半段（<c>Claude Code @ {DisplayName}</c>）；空 = 目标标识。</summary>
    public string? DisplayName { get; init; }

    /// <summary>目标上可用的 CLI。</summary>
    public IReadOnlyList<CliTargetRuntime> Runtimes { get; init; } = [];

    /// <summary>目标的宿主信息 JSON（OS、架构等），仅供展示。</summary>
    public string? HostInfoJson { get; init; }
}

/// <summary>目标上的一个 CLI。</summary>
public sealed record CliTargetRuntime
{
    /// <summary>provider 键（如 <c>claude</c>）。</summary>
    public required string ProviderKey { get; init; }

    /// <summary>目标上的可执行文件路径。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>CLI 报出的版本（<c>--version</c> 的输出原样即可）。</summary>
    public string? Version { get; init; }
}

/// <summary>登记的结果。</summary>
public sealed record CliTargetRegistrationResult
{
    /// <summary>这个目标上登记好的运行时（含版本不受支持的那些：它们照样登记，但上面的运行不跑）。</summary>
    public IReadOnlyList<CliRuntimeDto> Runtimes { get; init; } = [];

    /// <summary>这个部署不认识或没启用的 provider 键（没有登记）。</summary>
    public IReadOnlyList<string> UnknownProviders { get; init; } = [];

    /// <summary>版本不在部署支持范围内的 provider 键与原因。</summary>
    public IReadOnlyDictionary<string, string> UnsupportedVersions { get; init; } = new Dictionary<string, string>();
}

/// <inheritdoc cref="ICliRemoteRuntimeRegistry" />
public class CliRemoteRuntimeRegistry : ICliRemoteRuntimeRegistry
{
    /// <summary><c>CliRuntime.HostId</c> 的列宽。</summary>
    internal const int TargetIdMaxLength = 200;

    private readonly IRepository<Entities.CliRuntime, Guid> _repository;
    private readonly ICliProviderRegistry _providerRegistry;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly CliRunQueueSignal _queueSignal;
    private readonly ILogger<CliRemoteRuntimeRegistry> _logger;

    /// <summary>初始化远程运行时登记。</summary>
    public CliRemoteRuntimeRegistry(
        IRepository<Entities.CliRuntime, Guid> repository,
        ICliProviderRegistry providerRegistry,
        IOptionsMonitor<CliAgentOptions> options,
        CliRunQueueSignal queueSignal,
        ILogger<CliRemoteRuntimeRegistry> logger)
    {
        _repository = Check.NotNull(repository);
        _providerRegistry = Check.NotNull(providerRegistry);
        _options = Check.NotNull(options);
        _queueSignal = Check.NotNull(queueSignal);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<CliTargetRegistrationResult> RegisterAsync(
        CliTargetRegistration registration, CancellationToken cancellationToken = default)
    {
        Check.NotNull(registration);
        var targetId = ValidateTargetId(registration.TargetId);

        try
        {
            return await UpsertAsync(registration, targetId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 同一个目标的两次登记（或一次登记与一次入队时的占位）同时插入了同一行：重读一次就是更新
            _logger.LogDebug(ex, "Concurrent registration of CLI target {TargetId}; retrying as an update", targetId);
            return await UpsertAsync(registration, targetId, cancellationToken);
        }
    }

    private async Task<CliTargetRegistrationResult> UpsertAsync(
        CliTargetRegistration registration, string targetId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var existing = await _repository.AsQueryable(withTracking: true)
            .Where(r => r.HostId == targetId)
            .ToListAsync(cancellationToken);

        if (existing.Any(r => r.Mode == CliRuntimeMode.InProcess))
        {
            throw new ArgumentException(
                $"'{targetId}' is the host name of a server-local CLI runtime; use a distinct id for remote targets.", nameof(registration));
        }

        var unknown = new List<string>();
        var unsupported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var registered = new List<Entities.CliRuntime>();
        var inserted = new List<Entities.CliRuntime>();
        var displayName = string.IsNullOrWhiteSpace(registration.DisplayName) ? targetId : registration.DisplayName;

        foreach (var reported in registration.Runtimes)
        {
            if (_providerRegistry.Find(reported.ProviderKey) is not { Enabled: true } provider)
            {
                unknown.Add(reported.ProviderKey);
                continue;
            }

            var runtime = existing.FirstOrDefault(r => string.Equals(r.ProviderKey, provider.Key, StringComparison.OrdinalIgnoreCase));
            if (runtime is null)
            {
                runtime = NewRuntime(targetId, provider, _options.CurrentValue.MaxConcurrentRuns);
                await _repository.InsertAsync(runtime, cancellationToken);
                inserted.Add(runtime);
            }

            runtime.Name = $"{provider.DisplayName} @ {displayName}";
            runtime.ExecutablePath = Check.NotNullOrWhiteSpace(reported.ExecutablePath);
            runtime.CliVersion = reported.Version?.Trim();
            runtime.HostInfoJson = registration.HostInfoJson;
            runtime.LastSeenAt = now;
            runtime.ActivationRequestedAt = null;
            runtime.ActivationStartedAt = null;

            // 停用是管理员的决定，不因为「它连上来了」就复活
            if (runtime.Status != CliRuntimeStatus.Disabled)
            {
                runtime.Status = CliRuntimeStatus.Online;
            }

            if (CliVersionSupport.GetRejection(provider, runtime.CliVersion, targetId) is { } reason)
            {
                unsupported[provider.Key] = reason;
            }

            registered.Add(runtime);
        }

        foreach (var missing in existing.Where(r => !registered.Contains(r) && r.Status == CliRuntimeStatus.Online))
        {
            missing.Status = CliRuntimeStatus.Offline;
        }

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 插入失败的实体还是 Added：丢掉，重试时按更新那一行处理，不让下一次保存把它们重放一遍
            foreach (var runtime in inserted)
            {
                _repository.Discard(runtime);
            }

            throw;
        }

        if (unknown.Count > 0)
        {
            _logger.LogWarning("CLI target {TargetId} reported provider(s) this deployment does not run: {Providers}", targetId, unknown);
        }

        foreach (var (key, reason) in unsupported)
        {
            _logger.LogWarning("CLI target {TargetId} runs an unsupported version of {Provider}: {Reason}", targetId, key, reason);
        }

        // 等着这个目标的运行不必再等一个轮询间隔
        _queueSignal.Nudge();

        return new CliTargetRegistrationResult
        {
            Runtimes = registered.Select(r => CliRuntimeService.Project(r, _providerRegistry)).ToList(),
            UnknownProviders = unknown,
            UnsupportedVersions = unsupported
        };
    }

    /// <inheritdoc />
    public Task<int> MarkOfflineAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var id = ValidateTargetId(targetId);
        return _repository.AsQueryable()
            .Where(r => r.HostId == id && r.Mode == CliRuntimeMode.RemoteDaemon && r.Status == CliRuntimeStatus.Online)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRuntimeStatus.Offline), cancellationToken);
    }

    /// <summary>
    /// 取一个执行目标上某家 CLI 的运行时行，没有就以离线状态建出来（还不知道版本与路径，等目标上线登记时补上）。
    /// 落点（<see cref="CliRunPlacement.TargetId"/>）在目标从没上线过时也要有一行运行时可以挂运行、可以唤起。
    /// </summary>
    internal static async Task<Entities.CliRuntime> FindOrCreateAsync(
        IRepository<Entities.CliRuntime, Guid> repository, string targetId, CliProviderDescriptor provider, int maxConcurrentRuns,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(repository, targetId, provider.Key, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = NewRuntime(targetId, provider, maxConcurrentRuns);
        created.Name = $"{provider.DisplayName} @ {targetId}";
        try
        {
            await repository.InsertAsync(created, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 同时有人登记了它：用那一行。插入失败的实体还是 Added，丢掉它，免得下一次保存重放
            repository.Discard(created);
            return await FindAsync(repository, targetId, provider.Key, cancellationToken)
                   ?? throw new InvalidOperationException($"The CLI runtime for '{provider.Key}' on '{targetId}' could not be created.", ex);
        }
    }

    /// <summary>目标标识的形状：非空、不超过列宽、不是服务端机器名。返回去掉首尾空白的标识。</summary>
    internal static string ValidateTargetId(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("A remote CLI target id is required.", nameof(targetId));
        }

        var trimmed = targetId.Trim();
        if (trimmed.Length > TargetIdMaxLength)
        {
            throw new ArgumentException($"A remote CLI target id is at most {TargetIdMaxLength} characters.", nameof(targetId));
        }

        if (string.Equals(trimmed, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{trimmed}' is this server's host name, which identifies its local CLI runtimes; use a distinct id for remote targets.",
                nameof(targetId));
        }

        return trimmed;
    }

    private static Task<Entities.CliRuntime?> FindAsync(
        IRepository<Entities.CliRuntime, Guid> repository, string targetId, string providerKey, CancellationToken cancellationToken)
        => repository.AsQueryable()
            .FirstOrDefaultAsync(r => r.HostId == targetId && r.ProviderKey == providerKey, cancellationToken);

    private static Entities.CliRuntime NewRuntime(string targetId, CliProviderDescriptor provider, int maxConcurrentRuns) => new()
    {
        HostId = targetId,
        ProviderKey = provider.Key,
        Name = $"{provider.DisplayName} @ {targetId}",
        ExecutablePath = string.Empty,
        Mode = CliRuntimeMode.RemoteDaemon,
        Status = CliRuntimeStatus.Offline,
        MaxConcurrentRuns = maxConcurrentRuns
    };
}
