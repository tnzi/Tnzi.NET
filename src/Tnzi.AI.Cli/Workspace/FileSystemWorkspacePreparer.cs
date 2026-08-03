namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 在本机文件系统上布置工作区。
/// </summary>
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
    public async Task<CliWorkspace> PrepareAsync(CliRunContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(context);

        var root = BuildRunRoot(context);
        Directory.CreateDirectory(root);

        var userOwned = context.WorkDirectoryMode == CliWorkDirectoryMode.UserProvided
                        && !string.IsNullOrWhiteSpace(context.UserWorkDirectory);

        var workDirectory = userOwned
            ? context.UserWorkDirectory!
            : Path.Combine(root, CliWorkspaceLayout.WorkDirectoryName);

        if (userOwned && !Directory.Exists(workDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The configured user work directory '{workDirectory}' does not exist.");
        }

        Directory.CreateDirectory(workDirectory);

        // scratch 目录始终建在隔离根下，即使 cwd 是用户目录 ——
        // 框架的日志和产物不该落进别人的仓库。
        var outputDirectory = Path.Combine(root, CliWorkspaceLayout.OutputDirectoryName);
        var logDirectory = Path.Combine(root, CliWorkspaceLayout.LogDirectoryName);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(logDirectory);

        var sidecars = new List<string>();
        var metadataDirectory = Path.Combine(workDirectory, CliWorkspaceLayout.MetadataDirectoryName);
        var metadataExisted = Directory.Exists(metadataDirectory);
        Directory.CreateDirectory(metadataDirectory);
        if (!metadataExisted)
        {
            sidecars.Add(metadataDirectory);
        }

        await WriteRunMarkerAsync(root, metadataDirectory, context, sidecars, cancellationToken);
        await WriteContextSidecarAsync(metadataDirectory, context, sidecars, cancellationToken);

        var briefPath = await WriteBriefAsync(workDirectory, context, sidecars, cancellationToken);
        await MaterializeSkillsAsync(workDirectory, context, sidecars, cancellationToken);
        var mcpConfigPath = await WriteMcpConfigAsync(metadataDirectory, context, sidecars, cancellationToken);

        await WriteGcMetadataAsync(root, context, userOwned, cancellationToken);
        await WriteSidecarManifestAsync(root, sidecars, briefPath, cancellationToken);

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
            Sidecars = sidecars,
            WorkDirectoryIsUserOwned = userOwned
        };
    }

    /// <inheritdoc />
    public async Task<CliWorkspace?> ReuseAsync(
        string workDirectory, CliRunContext context, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(workDirectory);
        Check.NotNull(context);

        if (!Directory.Exists(workDirectory))
        {
            return null;
        }

        // 复用时只刷新上下文文件，不重建目录树 —— 续接会话的价值就在于 agent 已经在
        // 那个目录里干过活，删掉重来等于把它的工作成果扔了。
        var root = Directory.GetParent(workDirectory)?.FullName ?? workDirectory;
        return await PrepareAsync(context with
        {
            WorkDirectoryMode = CliWorkDirectoryMode.UserProvided,
            UserWorkDirectory = workDirectory
        }, cancellationToken) with
        {
            RootDirectory = root
        };
    }

    /// <inheritdoc />
    public async Task CleanupAsync(CliWorkspace workspace, bool removeAll, CancellationToken cancellationToken)
    {
        Check.NotNull(workspace);

        var briefPath = await ReadBriefPathAsync(workspace.RootDirectory, cancellationToken);
        if (briefPath is { } brief)
        {
            await BriefMarkerWriter.CleanupAsync(brief.Path, brief.CreatedByUs, cancellationToken);
        }

        // 按清单逐条回滚：先文件后目录（目录要空了才删得掉），
        // 目录按路径长度倒序 = 从最深的开始。
        foreach (var sidecar in workspace.Sidecars.Where(File.Exists))
        {
            TryDeleteFile(sidecar);
        }

        foreach (var sidecar in workspace.Sidecars
                     .Where(Directory.Exists)
                     .OrderByDescending(p => p.Length))
        {
            TryDeleteDirectory(sidecar);
        }

        if (!removeAll)
        {
            return;
        }

        // 用户提供的工作目录<b>永远不删</b>：它是别人的仓库。
        if (workspace.WorkDirectoryIsUserOwned)
        {
            TryDeleteDirectory(workspace.OutputDirectory, recursive: true);
            TryDeleteDirectory(workspace.LogDirectory, recursive: true);
            return;
        }

        TryDeleteDirectory(workspace.RootDirectory, recursive: true);
    }

    private string BuildRunRoot(CliRunContext context)
    {
        var root = _options.CurrentValue.WorkspacesRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = CliWorkspaceLayout.DefaultWorkspacesRoot;
        }

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

        return Path.Combine(root, tenantSegment, scopeSegment);
    }

    private static async Task WriteRunMarkerAsync(
        string root, string metadataDirectory, CliRunContext context, List<string> sidecars,
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

        var inWorkdir = Path.Combine(metadataDirectory, CliWorkspaceLayout.RunMarkerFileName);
        await WriteMarkerFileAsync(inWorkdir, payload, marker, sidecars, cancellationToken);

        // 同一标记也写在运行根目录：子进程若逃逸到 cwd 上层，向上查找仍能落在受管区内。
        var atRoot = Path.Combine(root, CliWorkspaceLayout.RunMarkerFileName);
        await WriteMarkerFileAsync(atRoot, payload, marker, sidecars, cancellationToken);
    }

    private static async Task WriteMarkerFileAsync(
        string path, string payload, CliRunMarker marker, List<string> sidecars, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            // 解析失败（半截写入）视为自有标记，回收重写；只有<b>能解析但归属他人</b>
            // 才拒绝覆盖 —— 那说明另一次运行正在用这个目录。
            var existing = await TryReadMarkerAsync(path, cancellationToken);
            if (existing is not null && IsForeignClaim(existing, marker))
            {
                throw new InvalidOperationException(
                    $"Workspace at '{Path.GetDirectoryName(path)}' is already claimed by run {existing.RunId}.");
            }
        }
        else
        {
            sidecars.Add(path);
        }

        await File.WriteAllTextAsync(path, payload, cancellationToken);
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

    private static async Task<CliRunMarker?> TryReadMarkerAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<CliRunMarker>(json, MetadataOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task WriteContextSidecarAsync(
        string metadataDirectory, CliRunContext context, List<string> sidecars, CancellationToken cancellationToken)
    {
        var path = Path.Combine(metadataDirectory, CliWorkspaceLayout.ContextFileName);
        if (!File.Exists(path))
        {
            sidecars.Add(path);
        }

        var payload = JsonSerializer.Serialize(new
        {
            runId = context.RunId,
            agentId = context.AgentId,
            tenantId = context.TenantId,
            provider = context.Provider.Key,
            skills = context.Skills.Select(s => s.Slug).ToArray()
        }, MetadataOptions);

        await File.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private async Task<BriefRecord?> WriteBriefAsync(
        string workDirectory, CliRunContext context, List<string> sidecars, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Provider.BriefFileName)
            || string.IsNullOrWhiteSpace(context.StableBrief))
        {
            return null;
        }

        var path = Path.Combine(workDirectory, context.Provider.BriefFileName);
        var created = await BriefMarkerWriter.WriteAsync(path, context.StableBrief, cancellationToken);
        if (created)
        {
            sidecars.Add(path);
        }

        return new BriefRecord(path, created);
    }

    private async Task MaterializeSkillsAsync(
        string workDirectory, CliRunContext context, List<string> sidecars, CancellationToken cancellationToken)
    {
        if (context.Skills.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(context.Provider.SkillsRelativePath))
        {
            _logger.LogInformation(
                "Provider '{Provider}' has no native skills directory; skipping materialization of {Count} skill(s)",
                context.Provider.Key, context.Skills.Count);
            return;
        }

        var skillsRoot = Path.Combine(
            workDirectory,
            context.Provider.SkillsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var rootExisted = Directory.Exists(skillsRoot);
        Directory.CreateDirectory(skillsRoot);
        if (!rootExisted)
        {
            sidecars.Add(skillsRoot);
        }

        foreach (var skill in context.Skills)
        {
            var slug = SanitizeSlug(skill.Slug);
            if (slug is null)
            {
                _logger.LogWarning("Skipping skill with unusable slug '{Slug}'", skill.Slug);
                continue;
            }

            var skillDirectory = Path.Combine(skillsRoot, slug);
            var existed = Directory.Exists(skillDirectory);
            Directory.CreateDirectory(skillDirectory);
            if (!existed)
            {
                sidecars.Add(skillDirectory);
            }

            var skillFile = Path.Combine(skillDirectory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                sidecars.Add(skillFile);
            }

            await File.WriteAllTextAsync(skillFile, BuildSkillMarkdown(slug, skill), cancellationToken);
        }
    }

    private async Task<string?> WriteMcpConfigAsync(
        string metadataDirectory, CliRunContext context, List<string> sidecars, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.McpConfigJson))
        {
            return null;
        }

        var path = Path.Combine(metadataDirectory, CliWorkspaceLayout.McpConfigFileName);
        if (!File.Exists(path))
        {
            sidecars.Add(path);
        }

        await File.WriteAllTextAsync(path, context.McpConfigJson, cancellationToken);
        return path;
    }

    private static async Task WriteGcMetadataAsync(
        string root, CliRunContext context, bool userOwned, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, CliWorkspaceLayout.GcMetadataFileName);
        var payload = JsonSerializer.Serialize(new CliWorkspaceGcMetadata
        {
            RunId = context.RunId,
            TenantId = context.TenantId,
            CreatedAt = DateTime.UtcNow,
            UserOwnedWorkDirectory = userOwned
        }, MetadataOptions);

        await File.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private static async Task WriteSidecarManifestAsync(
        string root, List<string> sidecars, BriefRecord? brief, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, CliWorkspaceLayout.SidecarManifestFileName);
        var payload = JsonSerializer.Serialize(new SidecarManifest
        {
            Sidecars = sidecars,
            BriefPath = brief?.Path,
            BriefCreatedByUs = brief?.CreatedByUs ?? false
        }, MetadataOptions);

        await File.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private static async Task<BriefRecord?> ReadBriefPathAsync(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, CliWorkspaceLayout.SidecarManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var manifest = JsonSerializer.Deserialize<SidecarManifest>(json, MetadataOptions);
            return string.IsNullOrWhiteSpace(manifest?.BriefPath)
                ? null
                : new BriefRecord(manifest.BriefPath, manifest.BriefCreatedByUs);
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

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete sidecar file {Path}", path);
        }
    }

    private void TryDeleteDirectory(string path, bool recursive = false)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
            }
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
