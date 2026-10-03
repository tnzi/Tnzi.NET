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

    /// <summary>框架自己的元数据目录（放在 cwd 内，agent 看得见但不需要理会）。</summary>
    public const string MetadataDirectoryName = ".tnzi";

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
    /// 专用配置目录的默认父目录名（<c>{WorkspacesRoot}/.cli-config/{provider}</c>）。
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

    /// <summary>生效的工作区根目录：配置值，否则 <see cref="DefaultWorkspacesRoot"/>。</summary>
    public static string ResolveWorkspacesRoot(CliAgentOptions options)
        => string.IsNullOrWhiteSpace(options.WorkspacesRoot) ? DefaultWorkspacesRoot : options.WorkspacesRoot;

    /// <summary>
    /// provider 的专用配置目录：显式配置值，否则 <c>{WorkspacesRoot}/.cli-config/{Key}</c>。
    /// </summary>
    public static string ResolveConfigDirectory(CliProviderDescriptor provider, CliAgentOptions options)
        => string.IsNullOrWhiteSpace(provider.ConfigDirectory)
            ? Path.Combine(ResolveWorkspacesRoot(options), ConfigDirectoryName, provider.Key)
            : provider.ConfigDirectory;

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
/// 用途是 <b>fail-closed</b>：外部 agent 可能经框架的 MCP server 回写平台。
/// 如果子进程被剥掉了全部 <c>TNZI_*</c> 环境变量（用户在 brief 里让它 <c>env -i</c> 重跑什么东西），
/// 回写工具必须能从 cwd 向上找到这个标记确认「我在一次受管运行里」，
/// 而不是退回去用调用者的个人凭据。
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

    /// <summary>创建时间。</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    /// <summary>工作目录是否属于用户（属于则永不删除）。</summary>
    [JsonPropertyName("userOwnedWorkDirectory")]
    public bool UserOwnedWorkDirectory { get; init; }
}
