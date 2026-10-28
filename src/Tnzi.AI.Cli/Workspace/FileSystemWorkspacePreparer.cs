namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 在执行目标的文件系统上布置工作区。
/// </summary>
/// <remarks>
/// 所有读写都经 <see cref="ICliHostTarget.FileSystem"/>，所有路径都按目标的 <see cref="CliHostPaths"/> 拼：
/// 本机目标就是 API 进程所在的磁盘，远程目标（容器）则是它自己的文件系统，框架不碰本机磁盘。
/// </remarks>
public class FileSystemWorkspacePreparer : ICliWorkspacePreparer
{
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        WriteIndented = true
    };

    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<FileSystemWorkspacePreparer> _logger;

    /// <summary>初始化工作区布置器。</summary>
    public FileSystemWorkspacePreparer(
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<FileSystemWorkspacePreparer> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<CliWorkspace> PrepareAsync(ICliHostTarget target, CliRunContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(target);
        Check.NotNull(context);

        var root = BuildRunRoot(target, context);

        var userOwned = context.WorkDirectoryMode == CliWorkDirectoryMode.UserProvided
                        && !string.IsNullOrWhiteSpace(context.UserWorkDirectory);

        var workDirectory = userOwned
            ? context.UserWorkDirectory!
            : target.FileSystem.Paths.Combine(root, CliWorkspaceLayout.WorkDirectoryName);

        return await PrepareAtAsync(target, context, root, workDirectory, userOwned, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 复用时只刷新受管文件（身份哨兵、上下文、brief、技能、MCP 配置、回收元数据、sidecar 清单），不重建目录树：
    /// 续接会话的价值就在于 agent 已经在那个目录里干过活，删掉重来等于把它的工作成果扔了。
    /// </para>
    /// <para>
    /// ★ 在<b>原来的运行根</b>上刷新，并保持原来的归属：受管目录（PerThread / PerRun）的运行根就是工作目录的上一级、
    /// 工作目录不归用户；用户目录的运行根照常按运行定（与第一次布置是同一个）。此前一律当成「用户提供的目录」重新布置：
    /// 按运行 id 另建一个运行根，在那里写产物目录、身份哨兵、sidecar 清单与一份标着「用户所有」的回收元数据，
    /// 事后只把返回值里的根改回去。于是 PerThread 每次重新执行都多出一个永不回收的游离目录；PerRun 的新根恰好就是原根，
    /// 原根的回收元数据被改成「用户所有」，回收器从此只删产物与日志、整个工作目录永不回收；用户目录模式下返回的根是
    /// 用户仓库的上一级，清理时按那里找不到的清单回滚，注入进用户记忆文件的 brief 块就留在了用户仓库里。
    /// </para>
    /// <para>
    /// 认不出来的目录不复用（返回 null，交给 <see cref="PrepareAsync"/> 重新布置）：受管模式下它必须是工作区根之下的
    /// <c>workdir</c> 目录，用户目录模式下它必须就是绑定配置的那个目录。绑定在运行被打回队列期间换了模式或目录时，
    /// 把旧目录的上一级当成运行根去写元数据，会写进用户仓库之外的任意位置。
    /// </para>
    /// </remarks>
    public async Task<CliWorkspace?> ReuseAsync(
        ICliHostTarget target, string workDirectory, CliRunContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(target);
        Check.NotNullOrWhiteSpace(workDirectory);
        Check.NotNull(context);

        var fileSystem = target.FileSystem;
        if (!await fileSystem.DirectoryExistsAsync(workDirectory, cancellationToken))
        {
            return null;
        }

        var paths = fileSystem.Paths;
        if (context.WorkDirectoryMode == CliWorkDirectoryMode.UserProvided)
        {
            return paths.AreSame(workDirectory, context.UserWorkDirectory)
                ? await PrepareAsync(target, context, cancellationToken)
                : null;
        }

        var workspacesRoot = target.WorkspacesRoot;
        var fullWorkDirectory = paths.GetFullPath(workDirectory);
        var root = paths.GetDirectoryName(fullWorkDirectory);
        if (string.IsNullOrEmpty(root)
            || !string.Equals(paths.GetFileName(fullWorkDirectory), CliWorkspaceLayout.WorkDirectoryName, StringComparison.Ordinal)
            || !paths.IsUnder(root, workspacesRoot))
        {
            _logger.LogInformation(
                "Not reusing {WorkDirectory} for run {RunId}: it is not a managed work directory under {WorkspacesRoot}",
                workDirectory, context.RunId, workspacesRoot);
            return null;
        }

        return await PrepareAtAsync(target, context, root, fullWorkDirectory, userOwned: false, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 运行根的认法与 <see cref="ReuseAsync"/> 相同（受管模式是工作目录的上一级，用户目录模式按运行定），
    /// 回滚清单取自布置时写在运行根上的 sidecar 清单。
    /// </remarks>
    public async Task<CliWorkspace?> OpenAsync(
        ICliHostTarget target, string workDirectory, CliRunContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(target);
        Check.NotNullOrWhiteSpace(workDirectory);
        Check.NotNull(context);

        var fileSystem = target.FileSystem;
        if (!await fileSystem.DirectoryExistsAsync(workDirectory, cancellationToken))
        {
            return null;
        }

        var paths = fileSystem.Paths;
        string root;
        var userOwned = context.WorkDirectoryMode == CliWorkDirectoryMode.UserProvided;
        if (userOwned)
        {
            if (!paths.AreSame(workDirectory, context.UserWorkDirectory))
            {
                return null;
            }

            root = BuildRunRoot(target, context);
        }
        else
        {
            workDirectory = paths.GetFullPath(workDirectory);
            var parent = paths.GetDirectoryName(workDirectory);
            if (string.IsNullOrEmpty(parent)
                || !string.Equals(paths.GetFileName(workDirectory), CliWorkspaceLayout.WorkDirectoryName, StringComparison.Ordinal)
                || !paths.IsUnder(parent, target.WorkspacesRoot))
            {
                return null;
            }

            root = parent;
        }

        if (await ReadManifestAsync(fileSystem, root, cancellationToken) is not { } manifest)
        {
            return null;
        }

        var metadataDirectory = ResolveMetadataDirectory(paths, root, workDirectory, userOwned);
        var mcpConfigPath = paths.Combine(metadataDirectory, CliWorkspaceLayout.McpConfigFileName);
        if (userOwned && !await fileSystem.FileExistsAsync(mcpConfigPath, cancellationToken))
        {
            // 这一版之前起的进程：它的受管文件还在用户目录里
            mcpConfigPath = paths.Combine(workDirectory, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.McpConfigFileName);
        }

        var systemPromptPath = paths.Combine(metadataDirectory, CliWorkspaceLayout.SystemPromptFileName);
        var contextDirectory = paths.Combine(metadataDirectory, CliWorkspaceLayout.ContextDirectoryName);
        return new CliWorkspace
        {
            RootDirectory = root,
            WorkDirectory = workDirectory,
            OutputDirectory = paths.Combine(root, CliWorkspaceLayout.OutputDirectoryName),
            LogDirectory = paths.Combine(root, CliWorkspaceLayout.LogDirectoryName),
            McpConfigPath = await fileSystem.FileExistsAsync(mcpConfigPath, cancellationToken) ? mcpConfigPath : null,
            SystemPromptFilePath = userOwned && await fileSystem.FileExistsAsync(systemPromptPath, cancellationToken) ? systemPromptPath : null,
            ContextDirectory = userOwned && await fileSystem.DirectoryExistsAsync(contextDirectory, cancellationToken) ? contextDirectory : null,
            Sidecars = manifest.Sidecars,
            WorkDirectoryIsUserOwned = userOwned
        };
    }

    /// <summary>
    /// 受管文件（上下文、MCP 配置、追加系统提示、技能）所在的目录。
    /// </summary>
    /// <remarks>
    /// 受管工作目录归框架所有，放在 cwd 内的 <c>.tnzi</c>。用户提供的工作目录（例如团队仓库的一个 worktree）里一个都不放，
    /// 放在运行根下：MCP 配置里有这次运行的回写凭据原文，写在用户仓库里，一次 <c>git add -A</c> 就会连凭据一起提交出去。
    /// </remarks>
    private static string ResolveMetadataDirectory(CliHostPaths paths, string root, string workDirectory, bool userOwned)
        => paths.Combine(userOwned ? root : workDirectory, CliWorkspaceLayout.MetadataDirectoryName);

    /// <summary>在给定的运行根与工作目录上布置（或刷新）本次运行的受管文件。</summary>
    /// <remarks>
    /// 用户提供的工作目录只读不写：说明经追加系统提示文件、技能经附加目录交给 CLI（provider 不支持时说明内联进新会话的第一条提示、
    /// 技能不物化），占用标记记在工作区根下按目录路径取名的文件里。
    /// </remarks>
    private async Task<CliWorkspace> PrepareAtAsync(
        ICliHostTarget target, CliRunContext context, string root, string workDirectory, bool userOwned,
        CancellationToken cancellationToken)
    {
        var fileSystem = target.FileSystem;
        var paths = fileSystem.Paths;
        await fileSystem.CreateDirectoryAsync(root, cancellationToken);

        if (userOwned && !await fileSystem.DirectoryExistsAsync(workDirectory, cancellationToken))
        {
            throw new DirectoryNotFoundException(
                $"The configured user work directory '{workDirectory}' does not exist.");
        }

        await fileSystem.CreateDirectoryAsync(workDirectory, cancellationToken);

        // 记忆文件能不能注入并逐字节还原，在往工作目录写任何东西之前就判定
        if (!userOwned && ResolveBriefPath(paths, workDirectory, context) is { } plannedBriefPath)
        {
            await BriefMarkerWriter.EnsureInjectableAsync(fileSystem, plannedBriefPath, cancellationToken);
        }

        // scratch 目录始终建在隔离根下，即使 cwd 是用户目录 ——
        // 框架的日志和产物不该落进别人的仓库。
        var outputDirectory = paths.Combine(root, CliWorkspaceLayout.OutputDirectoryName);
        var logDirectory = paths.Combine(root, CliWorkspaceLayout.LogDirectoryName);
        await fileSystem.CreateDirectoryAsync(outputDirectory, cancellationToken);
        await fileSystem.CreateDirectoryAsync(logDirectory, cancellationToken);

        var sidecars = new List<string>();
        var metadataDirectory = ResolveMetadataDirectory(paths, root, workDirectory, userOwned);
        await CreateTrackedDirectoryAsync(fileSystem, metadataDirectory, sidecars, cancellationToken);

        // 占用标记：受管目录写在 cwd 内的元数据目录；用户目录写在工作区根下按目录路径取名的文件
        //（每次运行的运行根各不相同，按运行根记就认不出两条线程在用同一个目录）
        string claimPath;
        if (userOwned)
        {
            // 目录里已有的标记照样认（只读不写）：这个目录可能是别的线程的受管工作目录，或者是这一版之前的运行留下的
            await EnsureNotClaimedInsideAsync(fileSystem, workDirectory, context, cancellationToken);

            claimPath = CliWorkspaceLayout.ResolveClaimPath(target, workDirectory);
            await fileSystem.CreateDirectoryAsync(paths.GetDirectoryName(claimPath)!, cancellationToken);
        }
        else
        {
            claimPath = paths.Combine(metadataDirectory, CliWorkspaceLayout.RunMarkerFileName);
        }

        await WriteRunMarkerAsync(fileSystem, root, claimPath, workDirectory, context, sidecars, cancellationToken);
        await WriteContextSidecarAsync(fileSystem, metadataDirectory, context, sidecars, cancellationToken);

        BriefRecord? briefPath = null;
        string? systemPromptPath = null;
        string? contextDirectory = null;
        if (userOwned)
        {
            systemPromptPath = await WriteSystemPromptFileAsync(fileSystem, metadataDirectory, context, sidecars, cancellationToken);
            contextDirectory = await MaterializeSkillsOutsideAsync(fileSystem, metadataDirectory, context, sidecars, cancellationToken);
        }
        else
        {
            briefPath = await WriteBriefAsync(fileSystem, workDirectory, context, sidecars, cancellationToken);
            await MaterializeSkillsAsync(fileSystem, workDirectory, context, sidecars, cancellationToken);
        }

        var mcpConfigPath = await WriteMcpConfigAsync(fileSystem, metadataDirectory, context, sidecars, cancellationToken);

        await WriteGcMetadataAsync(fileSystem, root, context, userOwned, cancellationToken);
        await WriteSidecarManifestAsync(fileSystem, root, sidecars, briefPath, cancellationToken);

        _logger.LogDebug(
            "Prepared workspace for run {RunId} at {Root} (cwd={WorkDirectory}, userOwned={UserOwned})",
            context.RunId, root, workDirectory, userOwned);

        return new CliWorkspace
        {
            RootDirectory = root,
            WorkDirectory = workDirectory,
            OutputDirectory = outputDirectory,
            LogDirectory = logDirectory,
            McpConfigPath = mcpConfigPath,
            McpConfigJson = mcpConfigPath is null ? null : context.McpConfigJson,
            SystemPromptFilePath = systemPromptPath,
            ContextDirectory = contextDirectory,
            Sidecars = sidecars,
            WorkDirectoryIsUserOwned = userOwned
        };
    }

    /// <summary>建目录；它和它之上原本不存在的各级都登记为 sidecar（清理时从最深的删起）。</summary>
    /// <remarks>只在 <paramref name="directory"/> 本身不存在时向上找：中间的各级目录也是这次建的，不登记就会在清理之后留下空壳。</remarks>
    private static async Task CreateTrackedDirectoryAsync(
        ICliHostFileSystem fileSystem, string directory, List<string> sidecars, CancellationToken cancellationToken)
    {
        var created = new List<string>();
        for (var current = directory;
             !string.IsNullOrEmpty(current) && !await fileSystem.DirectoryExistsAsync(current, cancellationToken);
             current = fileSystem.Paths.GetDirectoryName(current))
        {
            created.Add(current);
        }

        await fileSystem.CreateDirectoryAsync(directory, cancellationToken);
        sidecars.AddRange(created);
    }

    /// <inheritdoc />
    public async Task CleanupAsync(ICliHostTarget target, CliWorkspace workspace, bool removeAll, CancellationToken cancellationToken)
    {
        Check.NotNull(target);
        Check.NotNull(workspace);

        var fileSystem = target.FileSystem;
        var briefPath = await ReadBriefPathAsync(fileSystem, workspace.RootDirectory, cancellationToken);
        if (briefPath is { } brief)
        {
            await BriefMarkerWriter.CleanupAsync(fileSystem, brief.Path, brief.CreatedByUs, cancellationToken);
        }

        // 按清单逐条回滚：先文件后目录（目录要空了才删得掉），
        // 目录按路径长度倒序 = 从最深的开始。
        var directories = new List<string>();
        foreach (var sidecar in workspace.Sidecars)
        {
            if (await fileSystem.FileExistsAsync(sidecar, cancellationToken))
            {
                await TryDeleteFileAsync(fileSystem, sidecar);
            }
            else if (await fileSystem.DirectoryExistsAsync(sidecar, cancellationToken))
            {
                directories.Add(sidecar);
            }
        }

        foreach (var sidecar in directories.OrderByDescending(p => p.Length))
        {
            await TryDeleteDirectoryAsync(fileSystem, sidecar);
        }

        await TouchGcMetadataAsync(fileSystem, workspace.RootDirectory);

        if (!removeAll)
        {
            return;
        }

        // 用户提供的工作目录<b>永远不删</b>：它是别人的仓库。
        if (workspace.WorkDirectoryIsUserOwned)
        {
            await TryDeleteDirectoryAsync(fileSystem, workspace.OutputDirectory, recursive: true);
            await TryDeleteDirectoryAsync(fileSystem, workspace.LogDirectory, recursive: true);
            return;
        }

        await TryDeleteDirectoryAsync(fileSystem, workspace.RootDirectory, recursive: true);
    }

    private string BuildRunRoot(ICliHostTarget target, CliRunContext context)
    {
        var options = _options.CurrentValue;
        var paths = target.FileSystem.Paths;
        var root = target.WorkspacesRoot;
        var tenantSegment = context.TenantId?.ToString("N") ?? "host";

        // 目录的划分粒度就是「连续性」本身：编码 CLI 按 cwd 存会话存档，所以同一个 cwd
        // 反复出现才谈得上续接，换目录就等于换一段人生。
        //   PerThread   —— 按线程（默认）。没有线程的一次性任务退回按运行，它本就没有下一轮。
        //   PerRun      —— 永远按运行。选它就是要「每次从干净状态开始」，不连续是目的不是副作用。
        //   UserProvided—— 工作目录是用户自己的（跨轮天然稳定），这里的 root 只放本次运行的
        //                  元数据与产物，按运行分最干净。
        var scopeSegment = context.WorkDirectoryMode == CliWorkDirectoryMode.PerThread
            ? context.ThreadId?.ToString("N") ?? context.RunId.ToString("N")
            : context.RunId.ToString("N");

        return options.PartitionWorkspacesByUser
            ? paths.Combine(root, tenantSegment, CliWorkspaceLayout.UserPartition(context.UserId), scopeSegment)
            : paths.Combine(root, tenantSegment, scopeSegment);
    }

    /// <param name="fileSystem">执行目标的文件系统。</param>
    /// <param name="root">运行根。</param>
    /// <param name="claimPath">工作目录的占用标记（受管目录在 cwd 的元数据目录里，用户目录在工作区根下的占用目录里）。</param>
    /// <param name="workDirectory">被占用的工作目录（拒绝时报给调用方）。</param>
    /// <param name="context">这次运行。</param>
    /// <param name="sidecars">清理清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task WriteRunMarkerAsync(
        ICliHostFileSystem fileSystem, string root, string claimPath, string workDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        var marker = new CliRunMarker
        {
            RunId = context.RunId,
            AgentId = context.AgentId,
            TenantId = context.TenantId,
            ThreadId = context.ThreadId,
            CreatedAt = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(marker, MetadataOptions);

        await WriteMarkerFileAsync(fileSystem, claimPath, workDirectory, payload, marker, context.ReclaimableClaimRunId, sidecars, cancellationToken);

        // 同一标记也写在运行根目录：回收器按它判断这个运行目录此刻归谁占用。
        var atRoot = fileSystem.Paths.Combine(root, CliWorkspaceLayout.RunMarkerFileName);
        await WriteMarkerFileAsync(fileSystem, atRoot, root, payload, marker, context.ReclaimableClaimRunId, sidecars, cancellationToken);
    }

    /// <summary>
    /// 用户提供的工作目录里若有一个归属别人的占用标记（<c>.tnzi/run-marker.json</c>），拒绝；只读，不改它。
    /// </summary>
    private static async Task EnsureNotClaimedInsideAsync(
        ICliHostFileSystem fileSystem, string workDirectory, CliRunContext context, CancellationToken cancellationToken)
    {
        var path = fileSystem.Paths.Combine(workDirectory, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.RunMarkerFileName);
        if (!await fileSystem.FileExistsAsync(path, cancellationToken)
            || await TryReadMarkerAsync(fileSystem, path, cancellationToken) is not { } existing)
        {
            return;
        }

        var mine = new CliRunMarker { RunId = context.RunId, AgentId = context.AgentId, TenantId = context.TenantId, ThreadId = context.ThreadId };
        if (IsForeignClaim(existing, mine) && existing.RunId != context.ReclaimableClaimRunId)
        {
            throw new CliWorkspaceClaimedException(workDirectory, existing.RunId);
        }
    }

    private static async Task WriteMarkerFileAsync(
        ICliHostFileSystem fileSystem, string path, string claimedDirectory, string payload, CliRunMarker marker, Guid? reclaimableClaimRunId,
        List<string> sidecars, CancellationToken cancellationToken)
    {
        if (await fileSystem.FileExistsAsync(path, cancellationToken))
        {
            // 解析失败（半截写入）视为自有标记，回收重写；只有<b>能解析但归属他人</b>
            // 才拒绝覆盖，那说明另一次运行正在用这个目录。调度层已确认占用者结束了的除外（崩溃残留）。
            var existing = await TryReadMarkerAsync(fileSystem, path, cancellationToken);
            if (existing is not null && IsForeignClaim(existing, marker) && existing.RunId != reclaimableClaimRunId)
            {
                throw new CliWorkspaceClaimedException(claimedDirectory, existing.RunId);
            }
        }

        // 不论它此前在不在，覆盖之后它就是本次运行的标记，清理时必须删掉：标记在 = 有运行占着这个目录。
        // 此前「已存在就不登记」，一次硬崩溃留下的标记会被同线程以后每一轮覆盖、却永远不被删。
        sidecars.Add(path);

        await fileSystem.WriteAllTextAsync(path, payload, cancellationToken);
    }

    /// <summary>
    /// 现有标记是否属于<b>别人</b>。
    /// </summary>
    /// <remarks>
    /// 归属者是<b>线程</b>（有线程时）而不是运行：按线程分目录后，
    /// 同一会话的每一轮都是新的 RunId 却合法地回到同一个目录。
    /// <para>
    /// 按运行判会造成一个难查的故障：一次运行硬崩溃（进程被杀、宿主挂掉）时
    /// 清理不会发生，残留的标记会让<b>这个会话的以后每一轮</b>都被拒——
    /// 按运行分目录时这只影响那次死掉的运行，按线程分之后它会毒死整个会话。
    /// </para>
    /// <para>
    /// fail-closed 的内核没变：别的线程、别的 agent、别的租户仍然一律拒绝。
    /// </para>
    /// </remarks>
    private static bool IsForeignClaim(CliRunMarker existing, CliRunMarker mine)
    {
        if (mine.ThreadId is { } threadId && threadId != Guid.Empty)
        {
            // 没带线程的旧标记（升级前写的）按运行回退判定。
            return existing.ThreadId is { } existingThread && existingThread != Guid.Empty
                ? existingThread != threadId
                : existing.RunId != Guid.Empty && existing.RunId != mine.RunId;
        }

        return existing.RunId != Guid.Empty && existing.RunId != mine.RunId;
    }

    private static async Task<CliRunMarker?> TryReadMarkerAsync(
        ICliHostFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<CliRunMarker>(json, MetadataOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task WriteContextSidecarAsync(
        ICliHostFileSystem fileSystem, string metadataDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        // 元数据目录是框架自己的：已经在的（上一次运行崩溃留下的）也是我们的，一样登记，结束时删
        var path = fileSystem.Paths.Combine(metadataDirectory, CliWorkspaceLayout.ContextFileName);
        sidecars.Add(path);

        var payload = JsonSerializer.Serialize(new
        {
            runId = context.RunId,
            agentId = context.AgentId,
            tenantId = context.TenantId,
            provider = context.Provider.Key,
            skills = context.Skills.Select(s => s.Slug).ToArray()
        }, MetadataOptions);

        await fileSystem.WriteAllTextAsync(path, payload, cancellationToken);
    }

    /// <summary>本次运行要注入 brief 的记忆文件路径；provider 没有记忆文件或没有 brief 时为 null。</summary>
    private static string? ResolveBriefPath(CliHostPaths paths, string workDirectory, CliRunContext context)
        => string.IsNullOrWhiteSpace(context.Provider.BriefFileName) || string.IsNullOrWhiteSpace(context.StableBrief)
            ? null
            : paths.Combine(workDirectory, context.Provider.BriefFileName);

    private static async Task<BriefRecord?> WriteBriefAsync(
        ICliHostFileSystem fileSystem, string workDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        if (ResolveBriefPath(fileSystem.Paths, workDirectory, context) is not { } path)
        {
            return null;
        }

        var created = await BriefMarkerWriter.WriteAsync(fileSystem, path, context.StableBrief, cancellationToken);
        if (created)
        {
            sidecars.Add(path);
        }

        return new BriefRecord(path, created);
    }

    /// <summary>
    /// 用户提供的工作目录时：agent 说明写成运行根里的追加系统提示文件。provider 不能以文件接收追加系统提示、或没有说明时为 null
    /// （说明由执行器内联进新会话的第一条提示）。
    /// </summary>
    private static async Task<string?> WriteSystemPromptFileAsync(
        ICliHostFileSystem fileSystem, string metadataDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Provider.AppendSystemPromptFileArg) || string.IsNullOrWhiteSpace(context.StableBrief))
        {
            return null;
        }

        var path = fileSystem.Paths.Combine(metadataDirectory, CliWorkspaceLayout.SystemPromptFileName);
        sidecars.Add(path);

        // 每次运行原样重写：它在系统提示里，内容逐字节稳定才不作废 prompt 缓存（与记忆文件里的 brief 同一条约束）
        await fileSystem.WriteAllTextAsync(path, context.StableBrief, cancellationToken);
        return path;
    }

    /// <summary>
    /// 用户提供的工作目录时：技能物化在运行根里的附加目录下（<c>{元数据目录}/context/{provider 的技能目录}</c>），返回那个附加目录。
    /// provider 不支持附加目录时不物化（返回 null）：写进用户的仓库正是这里要避免的。
    /// </summary>
    private async Task<string?> MaterializeSkillsOutsideAsync(
        ICliHostFileSystem fileSystem, string metadataDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        if (context.Skills.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.Provider.AddDirectoryArg))
        {
            _logger.LogWarning(
                "Provider '{Provider}' cannot load skills from outside the work directory, and the work directory is user-provided; "
                + "skipping materialization of {Count} skill(s)",
                context.Provider.Key, context.Skills.Count);
            return null;
        }

        var contextDirectory = fileSystem.Paths.Combine(metadataDirectory, CliWorkspaceLayout.ContextDirectoryName);
        return await MaterializeSkillsAsync(fileSystem, contextDirectory, context, sidecars, cancellationToken)
            ? contextDirectory
            : null;
    }

    /// <summary>把技能写进 <paramref name="baseDirectory"/> 下 provider 的原生技能目录；物化了至少一个返回 true。</summary>
    private async Task<bool> MaterializeSkillsAsync(
        ICliHostFileSystem fileSystem, string baseDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        if (context.Skills.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.Provider.SkillsRelativePath))
        {
            _logger.LogInformation(
                "Provider '{Provider}' has no native skills directory; skipping materialization of {Count} skill(s)",
                context.Provider.Key, context.Skills.Count);
            return false;
        }

        var paths = fileSystem.Paths;
        var skillsRoot = paths.Combine(baseDirectory, context.Provider.SkillsRelativePath);
        await CreateTrackedDirectoryAsync(fileSystem, skillsRoot, sidecars, cancellationToken);
        var materialized = false;

        foreach (var skill in context.Skills)
        {
            var slug = SanitizeSlug(skill.Slug);
            if (slug is null)
            {
                _logger.LogWarning("Skipping skill with unusable slug '{Slug}'", skill.Slug);
                continue;
            }

            var skillDirectory = paths.Combine(skillsRoot, slug);
            var existed = await fileSystem.DirectoryExistsAsync(skillDirectory, cancellationToken);
            await fileSystem.CreateDirectoryAsync(skillDirectory, cancellationToken);
            if (!existed)
            {
                sidecars.Add(skillDirectory);
            }

            var skillFile = paths.Combine(skillDirectory, "SKILL.md");
            if (!await fileSystem.FileExistsAsync(skillFile, cancellationToken))
            {
                sidecars.Add(skillFile);
            }

            await fileSystem.WriteAllTextAsync(skillFile, BuildSkillMarkdown(slug, skill), cancellationToken);
            materialized = true;
        }

        return materialized;
    }

    private static async Task<string?> WriteMcpConfigAsync(
        ICliHostFileSystem fileSystem, string metadataDirectory, CliRunContext context, List<string> sidecars,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.McpConfigJson))
        {
            return null;
        }

        // 已经在的也登记：上一次运行崩溃留下的那份里有一枚（已失效的）回写凭据，不该一直留着
        var path = fileSystem.Paths.Combine(metadataDirectory, CliWorkspaceLayout.McpConfigFileName);
        sidecars.Add(path);

        await fileSystem.WriteAllTextAsync(path, context.McpConfigJson, cancellationToken);
        return path;
    }

    private static async Task WriteGcMetadataAsync(
        ICliHostFileSystem fileSystem, string root, CliRunContext context, bool userOwned, CancellationToken cancellationToken)
    {
        var path = fileSystem.Paths.Combine(root, CliWorkspaceLayout.GcMetadataFileName);
        var now = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new CliWorkspaceGcMetadata
        {
            RunId = context.RunId,
            TenantId = context.TenantId,
            ThreadId = context.ThreadId,
            CreatedAt = now,
            LastActiveAt = now,
            UserOwnedWorkDirectory = userOwned
        }, MetadataOptions);

        await fileSystem.WriteAllTextAsync(path, payload, cancellationToken);
    }

    /// <summary>
    /// 运行结束时把回收元数据的 <see cref="CliWorkspaceGcMetadata.LastActiveAt"/> 刷到现在：闲置从运行结束算起。
    /// </summary>
    /// <remarks>
    /// 只刷新已有的元数据，不新建；读写失败只记日志，清理是尽力而为，回收器另有「拿不准就不删」的兜底。
    /// 不带令牌：调用方在停机路径上也要走到这里。
    /// </remarks>
    private async Task TouchGcMetadataAsync(ICliHostFileSystem fileSystem, string root)
    {
        var path = fileSystem.Paths.Combine(root, CliWorkspaceLayout.GcMetadataFileName);

        try
        {
            if (!await fileSystem.FileExistsAsync(path, CancellationToken.None))
            {
                return;
            }

            var metadata = JsonSerializer.Deserialize<CliWorkspaceGcMetadata>(
                await fileSystem.ReadAllTextAsync(path, CancellationToken.None), MetadataOptions);
            if (metadata is null)
            {
                return;
            }

            var payload = JsonSerializer.Serialize(metadata with { LastActiveAt = DateTime.UtcNow }, MetadataOptions);
            await fileSystem.WriteAllTextAsync(path, payload, CancellationToken.None);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not refresh the GC metadata at {Path}", path);
        }
    }

    private static async Task WriteSidecarManifestAsync(
        ICliHostFileSystem fileSystem, string root, List<string> sidecars, BriefRecord? brief, CancellationToken cancellationToken)
    {
        var path = fileSystem.Paths.Combine(root, CliWorkspaceLayout.SidecarManifestFileName);
        var payload = JsonSerializer.Serialize(new SidecarManifest
        {
            Sidecars = sidecars,
            BriefPath = brief?.Path,
            BriefCreatedByUs = brief?.CreatedByUs ?? false
        }, MetadataOptions);

        await fileSystem.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private static async Task<BriefRecord?> ReadBriefPathAsync(
        ICliHostFileSystem fileSystem, string root, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(fileSystem, root, cancellationToken);
        return string.IsNullOrWhiteSpace(manifest?.BriefPath)
            ? null
            : new BriefRecord(manifest.BriefPath, manifest.BriefCreatedByUs);
    }

    /// <summary>读运行根上的 sidecar 清单；不在或读不出来时为 null。</summary>
    private static async Task<SidecarManifest?> ReadManifestAsync(
        ICliHostFileSystem fileSystem, string root, CancellationToken cancellationToken)
    {
        var path = fileSystem.Paths.Combine(root, CliWorkspaceLayout.SidecarManifestFileName);

        try
        {
            if (!await fileSystem.FileExistsAsync(path, cancellationToken))
            {
                return null;
            }

            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<SidecarManifest>(json, MetadataOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static string BuildSkillMarkdown(string slug, CliSkillPayload skill)
    {
        var description = string.IsNullOrWhiteSpace(skill.Description) ? slug : skill.Description.Trim();
        // 描述进 YAML frontmatter，必须是单行：换行会让整个 frontmatter 解析失败，
        // 而多数 CLI 对此的表现是静默忽略这个 skill。
        description = description.Replace("\r", string.Empty).Replace('\n', ' ');

        return $"""
            ---
            name: {slug}
            description: {JsonSerializer.Serialize(description)}
            ---

            {skill.Content}
            """;
    }

    /// <summary>
    /// 把 slug 收敛成安全的目录名。
    /// </summary>
    /// <remarks>
    /// 技能 slug 来自数据库，可能是用户填的。不清洗的话一个 <c>../../</c> 就能让
    /// 「往 skills 目录写文件」变成「往工作区外任意位置写文件」。
    /// </remarks>
    private static string? SanitizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var cleaned = new string(slug.Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());

        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private async Task TryDeleteFileAsync(ICliHostFileSystem fileSystem, string path)
    {
        try
        {
            await fileSystem.DeleteFileAsync(path, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete sidecar file {Path}", path);
        }
    }

    private async Task TryDeleteDirectoryAsync(ICliHostFileSystem fileSystem, string path, bool recursive = false)
    {
        try
        {
            await fileSystem.DeleteDirectoryAsync(path, recursive, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 非递归删除撞上"目录非空"是<b>预期</b>行为：说明 agent 在我们创建的目录里
            // 留下了自己的文件，那些不是我们的，不该顺手清掉。
            _logger.LogDebug(ex, "Could not delete sidecar directory {Path}", path);
        }
    }

    private sealed record BriefRecord(string Path, bool CreatedByUs);

    private sealed record SidecarManifest
    {
        public IReadOnlyList<string> Sidecars { get; init; } = [];
        public string? BriefPath { get; init; }
        public bool BriefCreatedByUs { get; init; }
    }
}
