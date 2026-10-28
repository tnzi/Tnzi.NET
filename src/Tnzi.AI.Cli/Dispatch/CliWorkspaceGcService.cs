namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 回收外部执行留下的工作区目录。
/// </summary>
/// <remarks>
/// <para>三档，各自回答一个不同的问题：</para>
/// <list type="bullet">
/// <item><b>完全清理</b>：运行已终态且闲置超过 <c>CompletedTtl</c> → 删整个运行目录。</item>
/// <item><b>孤儿清理</b>：目录里没有回收元数据（写入中途崩溃 / 手工残留）且超过
/// <c>OrphanTtl</c> → 删整个目录。缺元数据本身不能立刻删 —— 那正好是一个刚刚开始
/// 布置的目录的样子。</item>
/// <item><b>产物清理</b>：运行完成超过 <c>ArtifactTtl</c> 但会话可能还要续接 →
/// 只删可再生目录（<c>node_modules</c> 之类），保住 agent 的工作成果。</item>
/// </list>
/// <para>
/// 两条不可越界：<b>用户提供的工作目录永不删除</b>；<c>ArtifactPatterns</c> 只匹配
/// basename，含路径分隔符的条目静默丢弃 —— 否则一条 <c>"../.."</c> 就能把回收器变成删库工具。
/// </para>
/// <para>
/// ★ 「终态且闲置」两半都要有真实依据，三档都适用：
/// <b>终态</b>查运行表，这个目录的归属者（目录名、回收元数据与身份哨兵里记的运行 / 线程）只要还有一条
/// 排队中或执行中的运行就整个跳过；<b>闲置</b>从最近一次活动算起，取回收元数据的 <c>lastActiveAt</c>（运行开始与结束时写）、
/// 身份哨兵与目录本身修改时间中最晚的那个。此前只看根目录的 mtime：它只在根目录直接子项增删时变化，
/// agent 在 <c>workdir/</c> 里干活不刷新它，一次硬崩溃后它就冻结了，于是一条跑了 12 小时以上的运行会被删掉
/// <c>node_modules</c>/<c>bin</c>，一个仍在天天用的每线程工作区会在崩溃 24 小时后被整个删掉，哪怕此刻正有运行在里面执行。
/// 方向是失败关闭：运行表查不了，这一轮整个跳过。
/// </para>
/// </remarks>
public class CliWorkspaceGcService : BackgroundService
{
    private static readonly JsonSerializerOptions MetadataOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliWorkspaceGcService> _logger;

    /// <summary>初始化回收服务。</summary>
    public CliWorkspaceGcService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliWorkspaceGcService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AI:Cli:Enabled</c> 与 <c>AI:Cli:Gc:Enabled</c> 每轮重读：关着时按 <c>PollInterval</c> 空转等它打开，
    /// 而不是在启动时读一次就退出（运行期打开外部执行后，工作区就再也没人回收）。
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.Zero;
            try
            {
                var options = _options.CurrentValue;
                if (!options.Enabled || !options.Gc.Enabled)
                {
                    interval = options.PollInterval;
                }
                else
                {
                    interval = options.Gc.Interval;

                    // 先取运行表的快照再扫目录；查不了就抛到下面，整轮跳过（失败关闭）。
                    var activeOwners = await LoadActiveOwnersAsync(stoppingToken);
                    Collect(options, activeOwners);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workspace GC sweep failed; will retry on the next interval");
            }

            try
            {
                await Task.Delay(interval > TimeSpan.Zero ? interval : new CliAgentOptions().Gc.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 还没到终态的运行（排队、已认领、执行中）的 Id 与线程 Id，合成一个集合。
    /// </summary>
    /// <remarks>
    /// 跨租户查（这里没有当前租户，见 <see cref="CliRunQueries.AcrossTenants"/>）。排队中的也算：
    /// 被打回队列的运行会回到原目录续接，同一线程的下一轮也会落进这个目录。
    /// 只取两列，非终态的运行就是队列本身，量级很小。
    /// </remarks>
    internal async Task<IReadOnlySet<Guid>> LoadActiveOwnersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var active = await repository.AcrossTenants()
            .Where(r => r.Status == CliRunStatus.Queued
                        || r.Status == CliRunStatus.Dispatched
                        || r.Status == CliRunStatus.Running)
            .Select(r => new { r.Id, r.ThreadId })
            .ToListAsync(cancellationToken);

        var owners = new HashSet<Guid>();
        foreach (var run in active)
        {
            owners.Add(run.Id);
            if (run.ThreadId is { } threadId)
            {
                owners.Add(threadId);
            }
        }

        return owners;
    }

    /// <param name="options">回收配置。</param>
    /// <param name="activeOwners">还没到终态的运行的 Id 与线程 Id（见 <see cref="LoadActiveOwnersAsync"/>）。</param>
    /// <remarks>
    /// 目录形状有两种并存：<c>{tenant}/{run}</c> 与按用户分区的 <c>{tenant}/u-{user}/{run}</c>。
    /// 按目录名前缀而不是按当前开关识别分区层 —— 开关切换之后，旧形状的目录照样要被回收，
    /// 而把一个分区目录当成运行目录，会因为它没有回收元数据被判成孤儿、连同其下全部运行一起删掉。
    /// 点开头的目录（专用配置目录）一律不碰。
    /// </remarks>
    internal void Collect(CliAgentOptions options, IReadOnlySet<Guid> activeOwners)
    {
        Check.NotNull(activeOwners);

        var root = CliWorkspaceLayout.ResolveWorkspacesRoot(options);
        if (!Directory.Exists(root))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var artifactNames = options.Gc.ResolveArtifactPatterns()
            .Where(p => !p.Contains('/') && !p.Contains('\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var runDirectory in CliWorkspaceLayout.EnumerateRunRoots(root))
        {
            CollectRunDirectory(runDirectory, options, artifactNames, activeOwners, now);
        }

        foreach (var partition in CliWorkspaceLayout.EnumerateUserPartitions(root))
        {
            TryDeleteIfEmpty(partition);
        }
    }

    private void CollectRunDirectory(
        string runDirectory, CliAgentOptions options, HashSet<string> artifactNames,
        IReadOnlySet<Guid> activeOwners, DateTime now)
    {
        var metadataPath = Path.Combine(runDirectory, CliWorkspaceLayout.GcMetadataFileName);
        var metadata = ReadJson<CliWorkspaceGcMetadata>(metadataPath);
        var markerPath = Path.Combine(runDirectory, CliWorkspaceLayout.RunMarkerFileName);
        var marker = ReadJson<CliRunMarker>(markerPath);

        if (IsInUse(runDirectory, metadata, marker, activeOwners))
        {
            _logger.LogDebug("Skipping workspace {Directory}: a run that has not finished still owns it", runDirectory);
            return;
        }

        if (metadata is null)
        {
            // 没有元数据 = 孤儿。但刚开始布置的目录长得一模一样，所以必须给足时间。
            var age = now - Directory.GetCreationTimeUtc(runDirectory);
            if (age > options.Gc.OrphanTtl)
            {
                _logger.LogInformation("Removing orphaned workspace {Directory} (age {Age})", runDirectory, age);
                TryDelete(runDirectory);
            }

            return;
        }

        var idle = now - LastActivity(runDirectory, metadata, markerPath);

        if (idle > options.Gc.CompletedTtl)
        {
            if (metadata.UserOwnedWorkDirectory)
            {
                // cwd 是用户的仓库，只能删我们自己建的 scratch 目录，以及运行根里的受管文件
                //（崩溃时没来得及按清单删掉的 mcp.json 里有一枚已失效的回写凭据）。
                TryDelete(Path.Combine(runDirectory, CliWorkspaceLayout.OutputDirectoryName));
                TryDelete(Path.Combine(runDirectory, CliWorkspaceLayout.LogDirectoryName));
                TryDelete(Path.Combine(runDirectory, CliWorkspaceLayout.MetadataDirectoryName));
                return;
            }

            _logger.LogInformation("Removing completed workspace {Directory} (idle {Idle})", runDirectory, idle);
            TryDelete(runDirectory);
            return;
        }

        if (idle > options.Gc.ArtifactTtl)
        {
            CollectArtifacts(runDirectory, artifactNames);
        }
    }

    /// <summary>
    /// 在工作目录里逐层找名字命中的可再生目录并删掉。
    /// </summary>
    /// <remarks>
    /// 自己逐层走而不是一次 <see cref="SearchOption.AllDirectories"/>：后者照样枚举 <c>.git</c> 里面，
    /// 只是跳过名字恰好叫 <c>.git</c> 的那一个目录，于是 <c>.git/refs/heads/bin/</c>（一个叫 <c>bin/x</c> 的分支）
    /// 这类目录也会命中被删。这里 <c>.git</c> 子树真的不进入；命中删掉的目录不再往下走；
    /// 符号链接与目录联接（junction）既不删也不进入，它们指向的可能是工作区以外的任何地方。
    /// </remarks>
    private void CollectArtifacts(string runDirectory, HashSet<string> artifactNames)
    {
        var workDirectory = Path.Combine(runDirectory, CliWorkspaceLayout.WorkDirectoryName);
        if (!Directory.Exists(workDirectory))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(workDirectory);

        while (pending.Count > 0)
        {
            foreach (var directory in ListChildDirectories(pending.Pop()))
            {
                var name = Path.GetFileName(directory);

                // .git 子树永不进入：删掉它等于毁掉 agent 已经做完的全部工作。
                if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (artifactNames.Contains(name))
                {
                    _logger.LogDebug("Removing regenerable artifact directory {Directory}", directory);
                    TryDelete(directory);
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    /// <summary>一层子目录，不含符号链接与目录联接；读不了的目录当作没有子目录。</summary>
    private List<string> ListChildDirectories(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateDirectories()
                .Where(child => !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(child => child.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not enumerate {Directory}", directory);
            return [];
        }
    }

    /// <summary>
    /// 这个目录是否还归一条没到终态的运行所有。
    /// </summary>
    /// <remarks>
    /// 归属者的线索有三处，任何一处命中都算：目录名本身（按线程分时是线程 Id，按运行分时是运行 Id），
    /// 回收元数据里最近一次布置它的运行与线程，身份哨兵里正占着它的运行与线程。
    /// </remarks>
    private static bool IsInUse(
        string runDirectory, CliWorkspaceGcMetadata? metadata, CliRunMarker? marker, IReadOnlySet<Guid> activeOwners)
    {
        if (activeOwners.Count == 0)
        {
            return false;
        }

        Guid?[] owners =
        [
            Guid.TryParseExact(Path.GetFileName(runDirectory), "N", out var named) ? named : null,
            metadata?.RunId,
            metadata?.ThreadId,
            marker?.RunId,
            marker?.ThreadId
        ];

        return owners.Any(owner => owner is { } id && id != Guid.Empty && activeOwners.Contains(id));
    }

    /// <summary>
    /// 最近一次活动的时间：回收元数据的 <c>lastActiveAt</c> / <c>createdAt</c>、身份哨兵与目录本身的修改时间，取最晚。
    /// </summary>
    /// <remarks>
    /// 取最晚是失败关闭：哪一个信号说「刚刚还有动静」，就按它算。身份哨兵在一次运行开始布置时写下，
    /// 刚被认领、还在布置中的运行因此不会被这一轮当成闲置。
    /// </remarks>
    private static DateTime LastActivity(string runDirectory, CliWorkspaceGcMetadata metadata, string markerPath)
    {
        DateTime[] signals =
        [
            Directory.GetLastWriteTimeUtc(runDirectory),
            ToUtc(metadata.CreatedAt),
            metadata.LastActiveAt is { } lastActive ? ToUtc(lastActive) : DateTime.MinValue,
            File.Exists(markerPath) ? File.GetLastWriteTimeUtc(markerPath) : DateTime.MinValue
        ];

        return signals.Max();
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), MetadataOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TryDeleteIfEmpty(string path)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 与一次正在这个分区里布置的运行赛跑，下一轮再说。
            _logger.LogDebug(ex, "Could not remove empty user partition {Path}", path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete {Path}", path);
        }
    }
}
