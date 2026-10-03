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
/// </remarks>
public class CliWorkspaceGcService : BackgroundService
{
    private static readonly JsonSerializerOptions MetadataOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliWorkspaceGcService> _logger;

    /// <summary>初始化回收服务。</summary>
    public CliWorkspaceGcService(
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliWorkspaceGcService> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || !options.Gc.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Collect(_options.CurrentValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workspace GC sweep failed; will retry on the next interval");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.Gc.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <remarks>
    /// 目录形状有两种并存：<c>{tenant}/{run}</c> 与按用户分区的 <c>{tenant}/u-{user}/{run}</c>。
    /// 按目录名前缀而不是按当前开关识别分区层 —— 开关切换之后，旧形状的目录照样要被回收，
    /// 而把一个分区目录当成运行目录，会因为它没有回收元数据被判成孤儿、连同其下全部运行一起删掉。
    /// 点开头的目录（专用配置目录）一律不碰。
    /// </remarks>
    internal void Collect(CliAgentOptions options)
    {
        var root = CliWorkspaceLayout.ResolveWorkspacesRoot(options);
        if (!Directory.Exists(root))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var artifactNames = options.Gc.ArtifactPatterns
            .Where(p => !p.Contains('/') && !p.Contains('\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tenantDirectory in EnumerateManagedDirectories(root))
        {
            foreach (var child in EnumerateManagedDirectories(tenantDirectory))
            {
                if (!Path.GetFileName(child).StartsWith(CliWorkspaceLayout.UserPartitionPrefix, StringComparison.Ordinal))
                {
                    CollectRunDirectory(child, options, artifactNames, now);
                    continue;
                }

                foreach (var runDirectory in EnumerateManagedDirectories(child))
                {
                    CollectRunDirectory(runDirectory, options, artifactNames, now);
                }

                TryDeleteIfEmpty(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateManagedDirectories(string directory)
        => Directory.EnumerateDirectories(directory)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .ToList();

    private void CollectRunDirectory(
        string runDirectory, CliAgentOptions options, HashSet<string> artifactNames, DateTime now)
    {
        var metadataPath = Path.Combine(runDirectory, CliWorkspaceLayout.GcMetadataFileName);
        var metadata = ReadMetadata(metadataPath);

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

        var lastWrite = Directory.GetLastWriteTimeUtc(runDirectory);
        var idle = now - lastWrite;

        if (idle > options.Gc.CompletedTtl)
        {
            if (metadata.UserOwnedWorkDirectory)
            {
                // cwd 是用户的仓库，只能删我们自己建的 scratch 目录。
                TryDelete(Path.Combine(runDirectory, CliWorkspaceLayout.OutputDirectoryName));
                TryDelete(Path.Combine(runDirectory, CliWorkspaceLayout.LogDirectoryName));
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

    private void CollectArtifacts(string runDirectory, HashSet<string> artifactNames)
    {
        var workDirectory = Path.Combine(runDirectory, CliWorkspaceLayout.WorkDirectoryName);
        if (!Directory.Exists(workDirectory))
        {
            return;
        }

        foreach (var directory in EnumerateSafely(workDirectory))
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
            }
        }
    }

    private IEnumerable<string> EnumerateSafely(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not enumerate {Directory}", directory);
            return [];
        }
    }

    private CliWorkspaceGcMetadata? ReadMetadata(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CliWorkspaceGcMetadata>(File.ReadAllText(path), MetadataOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
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
