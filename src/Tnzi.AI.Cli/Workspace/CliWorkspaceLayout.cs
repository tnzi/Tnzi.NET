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

    /// <summary>回退用的默认工作区根目录。</summary>
    public static string DefaultWorkspacesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tnzi", "agent-workspaces");
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
