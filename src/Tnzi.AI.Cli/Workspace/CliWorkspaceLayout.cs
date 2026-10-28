namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 工作区目录与元数据文件的名称约定。
/// </summary>
public static class CliWorkspaceLayout
{
    /// <summary>agent 的 cwd（隔离模式）。</summary>
    public const string WorkDirectoryName = "workdir";

    /// <summary>产物落点。</summary>
    public const string OutputDirectoryName = "output";

    /// <summary>日志目录。</summary>
    public const string LogDirectoryName = "logs";

    /// <summary>
    /// 框架自己的元数据目录。受管工作目录里放在 cwd 内（agent 看得见但不需要理会）；
    /// 用户提供的工作目录里一个受管文件都不放，它在运行根下。
    /// </summary>
    public const string MetadataDirectoryName = ".tnzi";

    /// <summary>
    /// 用户提供的工作目录时，agent 说明（brief）写成的追加系统提示文件名（放在运行根的元数据目录里）。
    /// </summary>
    public const string SystemPromptFileName = "system-prompt.md";

    /// <summary>
    /// 用户提供的工作目录时，物化技能的目录名（运行根的元数据目录下），整个目录作为附加目录交给 CLI。
    /// </summary>
    public const string ContextDirectoryName = "context";

    /// <summary>
    /// 用户提供的工作目录的占用标记所在的目录（工作区根下）。
    /// </summary>
    /// <remarks>
    /// 同一个用户目录可能被多条线程的运行先后使用，而每次运行的运行根各不相同，占用只能按目录本身记：
    /// <c>{WorkspacesRoot}/.tnzi-claims/{目录路径的哈希}.json</c>。以点开头，回收器与运行根枚举都不进入。
    /// </remarks>
    public const string ClaimsDirectoryName = ".tnzi-claims";

    /// <summary>用户提供的工作目录在执行目标上的占用标记路径。</summary>
    /// <remarks>按规范化后的路径取哈希；路径不区分大小写的目标上先统一大小写，同一个目录换个写法也认得出。</remarks>
    public static string ResolveClaimPath(ICliHostTarget target, string workDirectory)
    {
        Check.NotNull(target);
        Check.NotNullOrWhiteSpace(workDirectory);

        var paths = target.FileSystem.Paths;
        var normalized = paths.GetFullPath(workDirectory);
        if (paths.Comparison == StringComparison.OrdinalIgnoreCase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..32];
        return paths.Combine(target.WorkspacesRoot, ClaimsDirectoryName, key + ".json");
    }

    /// <summary>结构化上下文 sidecar 文件名。</summary>
    public const string ContextFileName = "agent-context.json";

    /// <summary>身份哨兵文件名。</summary>
    public const string RunMarkerFileName = "run-marker.json";

    /// <summary>受管 MCP 配置文件名。</summary>
    public const string McpConfigFileName = "mcp.json";

    /// <summary>回收元数据文件名（放在运行根目录）。</summary>
    public const string GcMetadataFileName = ".tnzi-gc.json";

    /// <summary>本次布置创建的文件/目录清单（放在运行根目录）。</summary>
    public const string SidecarManifestFileName = ".tnzi-sidecars.json";

    /// <summary>身份哨兵里的固定标识。</summary>
    public const string ManagedBy = "tnzi-external-agent";

    /// <summary>
    /// 专用配置目录的默认父目录名（<c>{WorkspacesRoot}/.cli-config/{provider}</c>）；
    /// 每工作区配置目录也用这个名字（<c>{运行根}/.cli-config/{provider}</c>）。
    /// </summary>
    /// <remarks>
    /// 以点开头是契约：回收器跳过工作区根与租户层下所有点开头的目录。专用配置目录里是登录态与会话存档，
    /// 按运行目录的规矩回收它，等于每 72 小时把 CLI 登出一次、清空所有线程的续接指针。
    /// </remarks>
    public const string ConfigDirectoryName = ".cli-config";

    /// <summary>用户分区目录名前缀（<c>u-{userId}</c>），回收器据此识别出多一层的目录。</summary>
    public const string UserPartitionPrefix = "u-";

    /// <summary>没有登录用户的运行所在的分区名。</summary>
    public const string AnonymousUserPartition = UserPartitionPrefix + "anonymous";

    /// <summary>用户分区目录名。</summary>
    public static string UserPartition(Guid? userId)
        => userId is { } id ? UserPartitionPrefix + id.ToString("N") : AnonymousUserPartition;

    /// <summary>
    /// 工作区根下的全部运行根目录（目录名是线程或运行的 Id）。两种形状并存：<c>{tenant}/{scope}</c> 与按用户分区的
    /// <c>{tenant}/u-{user}/{scope}</c>。
    /// </summary>
    /// <remarks>
    /// 按目录名前缀而不是按当前开关识别分区层：开关切换之后两种形状的目录同时存在。点开头的目录（专用配置目录）一律不进入。
    /// 回收器与线程删除的清理都走这里，目录的形状只在这一处定义。
    /// </remarks>
    internal static IEnumerable<string> EnumerateRunRoots(string workspacesRoot)
    {
        foreach (var tenantDirectory in EnumerateManagedDirectories(workspacesRoot))
        {
            foreach (var child in EnumerateManagedDirectories(tenantDirectory))
            {
                if (!IsUserPartition(child))
                {
                    yield return child;
                    continue;
                }

                foreach (var runRoot in EnumerateManagedDirectories(child))
                {
                    yield return runRoot;
                }
            }
        }
    }

    /// <summary>工作区根下的全部用户分区目录（<c>{tenant}/u-{user}</c>）。</summary>
    internal static IEnumerable<string> EnumerateUserPartitions(string workspacesRoot)
        => EnumerateManagedDirectories(workspacesRoot)
            .SelectMany(EnumerateManagedDirectories)
            .Where(IsUserPartition);

    private static bool IsUserPartition(string directory)
        => Path.GetFileName(directory).StartsWith(UserPartitionPrefix, StringComparison.Ordinal);

    /// <summary>一层子目录，跳过点开头的。先整层列出再返回：调用方会边遍历边删。</summary>
    private static List<string> EnumerateManagedDirectories(string directory)
        => Directory.EnumerateDirectories(directory)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .ToList();

    /// <summary>生效的工作区根目录：配置值，否则 <see cref="DefaultWorkspacesRoot"/>。</summary>
    public static string ResolveWorkspacesRoot(CliAgentOptions options)
        => string.IsNullOrWhiteSpace(options.WorkspacesRoot) ? DefaultWorkspacesRoot : options.WorkspacesRoot;

    /// <summary>
    /// provider 在执行目标上的专用配置目录：显式配置值，否则 <c>{目标的工作区根}/.cli-config/{Key}</c>。
    /// </summary>
    /// <remarks>显式配置值是目标上的路径，原样使用。</remarks>
    public static string ResolveConfigDirectory(CliProviderDescriptor provider, ICliHostTarget target)
    {
        Check.NotNull(provider);
        Check.NotNull(target);
        return string.IsNullOrWhiteSpace(provider.ConfigDirectory)
            ? target.FileSystem.Paths.Combine(target.WorkspacesRoot, ConfigDirectoryName, provider.Key)
            : provider.ConfigDirectory;
    }

    /// <summary>
    /// <see cref="CliUserConfigIsolation.PerWorkspaceConfigDirectory"/> 时 provider 在这个工作区里的配置目录：
    /// <c>{运行根}/.cli-config/{Key}</c>。
    /// </summary>
    /// <remarks>
    /// 放在运行根下、工作目录之外：工作区被删时它一起删掉，又不出现在 agent 的项目树里。
    /// 回收器的产物清理只进工作目录，碰不到它。
    /// </remarks>
    public static string ResolveWorkspaceConfigDirectory(string runRoot, CliProviderDescriptor provider, CliHostPaths paths)
    {
        Check.NotNullOrWhiteSpace(runRoot);
        Check.NotNull(provider);
        Check.NotNull(paths);
        return paths.Combine(runRoot, ConfigDirectoryName, provider.Key);
    }

    /// <summary>
    /// 回退用的默认工作区根目录 <c>{LocalApplicationData}/Tnzi/agent-workspaces</c>；宿主没有用户数据目录时为空串
    /// （启用本模块时 <see cref="CliAgentOptionsValidator"/> 据此拒绝启动）。
    /// </summary>
    public static string DefaultWorkspacesRoot { get; } = ResolveDefaultWorkspacesRoot(Environment.GetFolderPath);

    /// <remarks>
    /// ★ 两条不变量：①必须 <see cref="Environment.SpecialFolderOption.DoNotVerify"/>，默认的 <c>None</c> 在 Unix 上
    /// 对尚不存在的目录返回空串（macOS 与新建账号上 <c>~/.local/share</c> 往往还没建）；②解析不出时返回空串而不是
    /// 拼出 <c>Tnzi/agent-workspaces</c> 这样的相对路径 —— 那会让外部 agent 的工作区静默落进进程当前目录，
    /// 通常就是部署目录。
    /// </remarks>
    internal static string ResolveDefaultWorkspacesRoot(Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> getFolderPath)
    {
        var localAppData = getFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrWhiteSpace(localAppData)
            ? string.Empty
            : Path.Combine(localAppData, "Tnzi", "agent-workspaces");
    }
}

/// <summary>
/// 身份哨兵内容。
/// </summary>
/// <remarks>
/// 记录「这个目录此刻归哪次运行（哪条线程）占用」，两处读它：工作区布置器据此拒绝在别的线程正占着的目录里
/// 再布置一次（fail-closed，见 <c>FileSystemWorkspacePreparer</c>），回收器据此跳过归属者还没结束的目录。
/// 每次运行结束都删掉；它在 = 有运行占着这个目录。框架不向子进程注入任何身份环境变量，回写通道的身份
/// 只来自 MCP 配置里那枚运行范围凭据，也没有任何代码靠向上查找这个标记来确认身份。
/// </remarks>
public sealed record CliRunMarker
{
    /// <summary>固定标识，见 <see cref="CliWorkspaceLayout.ManagedBy"/>。</summary>
    [JsonPropertyName("managedBy")]
    public string ManagedBy { get; init; } = CliWorkspaceLayout.ManagedBy;

    /// <summary>运行 ID。</summary>
    [JsonPropertyName("runId")]
    public Guid RunId { get; init; }

    /// <summary>Agent ID。</summary>
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; init; }

    /// <summary>租户。</summary>
    [JsonPropertyName("tenantId")]
    public Guid? TenantId { get; init; }

    /// <summary>会话线程（PerThread 模式下工作区的真正归属者）。</summary>
    /// <remarks>
    /// 占用冲突要按<b>归属者</b>判而不是按运行判：按线程分目录时，
    /// 同一线程的下一轮本就会合法地回到同一个目录。
    /// </remarks>
    [JsonPropertyName("threadId")]
    public Guid? ThreadId { get; init; }

    /// <summary>写入时间。</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 回收元数据。
/// </summary>
public sealed record CliWorkspaceGcMetadata
{
    /// <summary>运行 ID。</summary>
    [JsonPropertyName("runId")]
    public Guid RunId { get; init; }

    /// <summary>租户。</summary>
    [JsonPropertyName("tenantId")]
    public Guid? TenantId { get; init; }

    /// <summary>会话线程（PerThread 下这个目录的真正归属者；回收前据此查它是否还有未结束的运行）。</summary>
    [JsonPropertyName("threadId")]
    public Guid? ThreadId { get; init; }

    /// <summary>创建时间。</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 最近一次有运行在这里开始或结束的时间。回收的闲置时长从它算起。
    /// </summary>
    /// <remarks>
    /// 不能用目录的修改时间代替：目录 mtime 只在根目录的直接子项增删时变化，agent 在 <c>workdir/</c> 里干活、
    /// 覆盖写已存在的元数据都不刷新它，一次硬崩溃后它就冻结了。
    /// </remarks>
    [JsonPropertyName("lastActiveAt")]
    public DateTime? LastActiveAt { get; init; }

    /// <summary>工作目录是否属于用户（属于则永不删除）。</summary>
    [JsonPropertyName("userOwnedWorkDirectory")]
    public bool UserOwnedWorkDirectory { get; init; }
}
