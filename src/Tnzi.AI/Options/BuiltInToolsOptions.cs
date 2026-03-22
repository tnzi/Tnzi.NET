namespace Tnzi.AI.Options;

/// <summary>
/// 内置工具配置选项
/// </summary>
public class BuiltInToolsOptions
{
    /// <summary>
    /// 是否启用内置工具注册（默认关闭）
    /// </summary>
    /// <remarks>
    /// 内置工具（日期时间、数学计算、文本处理）的描述会占用 LLM 的 Token 空间，
    /// 而现代 LLM 已能原生处理这些操作，因此默认关闭以节省 Token 开销。
    /// 如需启用，在配置文件中设置 AI:BuiltInTools:Enabled = true
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 启用日期时间工具
    /// </summary>
    public bool EnableDateTime { get; set; } = true;

    /// <summary>
    /// 启用文本处理工具
    /// </summary>
    public bool EnableText { get; set; } = true;

    /// <summary>
    /// 启用 Web 搜索工具（需要注册 IWebSearchProvider）
    /// </summary>
    public bool EnableWebSearch { get; set; } = true;

    /// <summary>
    /// 是否启用 Memory 工具（save_memory, search_memory, delete_memory）
    /// </summary>
    public bool EnableMemory { get; set; } = true;
}
