namespace Tnzi.AI.Options;

/// <summary>
/// 上下文提供器配置选项
/// </summary>
/// <remarks>
/// 控制 IContextProvider 的行为，包括 Memory、RAG、Skills 等
/// </remarks>
public class ContextProvidersOptions
{
    /// <summary>
    /// 是否启用上下文提供器
    /// </summary>
    /// <remarks>
    /// 启用后，Agent 将使用 CompositeContextProvider 来注入额外上下文。
    /// 默认关闭以保持向后兼容。
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 聊天历史记忆配置（向量化 Chat History）
    /// </summary>
    public ChatHistoryMemoryOptions ChatHistoryMemory { get; set; } = new();

    /// <summary>
    /// 文本搜索/知识检索配置（RAG）
    /// </summary>
    public TextSearchOptions TextSearch { get; set; } = new();

    /// <summary>
    /// Skills 技能配置
    /// </summary>
    public SkillsOptions Skills { get; set; } = new();
}

/// <summary>
/// 聊天历史记忆配置选项
/// </summary>
/// <remarks>
/// 配置聊天历史记忆上下文提供器
/// </remarks>
public class ChatHistoryMemoryOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 搜索时机
    /// </summary>
    public ContextSearchTime SearchTime { get; set; } = ContextSearchTime.BeforeAIInvoke;

    /// <summary>
    /// 最大返回结果数
    /// </summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// 上下文注入提示词
    /// </summary>
    public string? ContextPrompt { get; set; }

    /// <summary>
    /// 向量集合名称
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// 向量维度
    /// </summary>
    public int? VectorDimensions { get; set; }
}

/// <summary>
/// 文本搜索/知识检索配置选项
/// </summary>
/// <remarks>
/// 配置 RAG 文本搜索上下文提供器
/// </remarks>
public class TextSearchOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 搜索时机
    /// </summary>
    public ContextSearchTime SearchTime { get; set; } = ContextSearchTime.BeforeAIInvoke;

    /// <summary>
    /// 最近消息记忆限制（避免反馈回路）
    /// </summary>
    public int RecentMessageMemoryLimit { get; set; } = 5;

    /// <summary>
    /// 上下文注入提示词
    /// </summary>
    public string? ContextPrompt { get; set; }

    /// <summary>
    /// 引用提示词
    /// </summary>
    public string? CitationsPrompt { get; set; }
}

/// <summary>
/// Skills 技能配置选项
/// </summary>
public class SkillsOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 技能文件搜索路径
    /// </summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>
    /// 允许的技能列表（为空则允许所有）
    /// </summary>
    public List<string> AllowList { get; set; } = [];

    /// <summary>
    /// 禁止的技能列表
    /// </summary>
    public List<string> DenyList { get; set; } = [];

    /// <summary>
    /// 是否启用 requires 检查（检查技能依赖的 bins/env/config/os）
    /// </summary>
    public bool RequireChecksEnabled { get; set; } = true;

    /// <summary>
    /// 技能注入模式
    /// </summary>
    /// <remarks>
    /// Instructions：全量注入到系统指令；OnDemandTools：暴露 skill_search/skill_get 工具；Both：两者都启用。
    /// </remarks>
    public SkillInjectionMode InjectionMode { get; set; } = SkillInjectionMode.OnDemandTools;
}

/// <summary>
/// 技能注入模式
/// </summary>
public enum SkillInjectionMode
{
    /// <summary>全量注入到 Instructions（当前行为）</summary>
    Instructions = 0,

    /// <summary>按需工具模式：暴露 skill_search/skill_get</summary>
    OnDemandTools = 1,

    /// <summary>两者都启用</summary>
    Both = 2
}

/// <summary>
/// 上下文搜索时机
/// </summary>
public enum ContextSearchTime
{
    /// <summary>
    /// 在 AI 调用前自动注入上下文
    /// </summary>
    BeforeAIInvoke = 0,

    /// <summary>
    /// 作为工具暴露给模型，按需调用
    /// </summary>
    OnDemandFunctionCalling = 1
}
