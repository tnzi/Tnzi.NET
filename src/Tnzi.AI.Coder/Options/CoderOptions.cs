namespace Tnzi.AI.Coder.Options;

/// <summary>
/// AI Coder 本地工具配置选项
/// </summary>
public class CoderOptions
{
    /// <summary>
    /// 项目根目录
    /// </summary>
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// 指令文件路径列表（后面的文件覆盖前面的）
    /// </summary>
    public List<string> InstructionFiles { get; set; } = ["TNZI.md", ".tnzi/TNZI.md"];

    /// <summary>
    /// 记忆存储目录（相对于 ProjectRoot）
    /// </summary>
    public string MemoryDirectory { get; set; } = ".tnzi/memory";

    /// <summary>
    /// 对话存储目录（相对于 ProjectRoot）
    /// </summary>
    public string ConversationDirectory { get; set; } = ".tnzi/conversations";

    /// <summary>
    /// 默认工具组列表
    /// </summary>
    public List<string> DefaultToolGroups { get; set; } = ["filesystem", "shell", "codesearch", "web", "memory", "project", "git", "diff", "process", "repl"];

    /// <summary>
    /// 最大工具迭代次数
    /// </summary>
    public int MaxToolIterations { get; set; } = 25;

    /// <summary>
    /// 沙箱安全选项
    /// </summary>
    public SandboxOptions Sandbox { get; set; } = new();
}
