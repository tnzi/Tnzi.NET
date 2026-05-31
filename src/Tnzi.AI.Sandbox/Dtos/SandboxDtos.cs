namespace Tnzi.AI.Sandbox.Dtos;

/// <summary>
/// Sandbox 状态 DTO
/// </summary>
public class SandboxStatusDto
{
    /// <summary>模块是否启用</summary>
    public bool Enabled { get; init; }

    /// <summary>当前 Provider 名称</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>数据根目录</summary>
    public string DataRoot { get; init; } = string.Empty;

    /// <summary>拒绝的命令列表</summary>
    public IReadOnlyList<string> DeniedCommands { get; init; } = [];

    /// <summary>拒绝的文件模式列表</summary>
    public IReadOnlyList<string> DeniedPatterns { get; init; } = [];

    /// <summary>环境变量黑名单</summary>
    public IReadOnlyList<string> EnvironmentBlacklist { get; init; } = [];
}
