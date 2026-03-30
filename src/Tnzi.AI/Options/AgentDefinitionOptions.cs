namespace Tnzi.AI.Options;

/// <summary>
/// YAML Agent 定义文件配置选项
/// </summary>
public class AgentDefinitionOptions
{
    /// <summary>
    /// 是否启用文件定义 Agent（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Agent 定义文件目录路径（相对于应用根目录，默认 "agents"）
    /// </summary>
    public string DirectoryPath { get; set; } = "agents";

    /// <summary>
    /// 是否监视文件变更（热重载，默认启用）
    /// </summary>
    public bool WatchForChanges { get; set; } = true;

    /// <summary>
    /// 启动时是否同步到数据库（默认启用）
    /// </summary>
    public bool SyncOnStartup { get; set; } = true;
}
