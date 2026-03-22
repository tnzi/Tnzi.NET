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
    /// 上下文注入的最大 token 预算（0 = 不限制，默认 16384）
    /// </summary>
    /// <remarks>
    /// 当所有 ContextProvider 的注入内容总 token 超过此预算时，
    /// 后续 Provider 的注入将被跳过。设为 0 则不进行 token 限制。
    /// 推荐范围：仅记忆/Skill 场景用 4096–8192；RAG 为主用 8192–16384；全功能场景用 16384–32768。
    /// </remarks>
    public int MaxTokenBudget { get; set; } = 16_384;

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

    /// <summary>
    /// 记忆存储配置
    /// </summary>
    public MemoryOptions Memory { get; set; } = new();

    /// <summary>
    /// 实体记忆配置（跨会话命名实体追踪）
    /// </summary>
    public EntityMemoryOptions EntityMemory { get; set; } = new();

    /// <summary>
    /// 项目上下文配置（通过 IProjectContextLoader 加载项目指令）
    /// </summary>
    public ProjectContextOptions ProjectContext { get; set; } = new();
}

/// <summary>
/// 项目上下文配置选项
/// </summary>
public class ProjectContextOptions
{
    /// <summary>
    /// 是否启用项目上下文注入
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// 记忆存储配置选项
/// </summary>
public class MemoryOptions
{
    /// <summary>
    /// 是否启用记忆上下文提供器
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认记忆 scope（用于用户级记忆）
    /// </summary>
    public string DefaultScope { get; set; } = "default";

    /// <summary>
    /// 全局共享记忆 scope（所有用户可见，不受 EnableUserIsolation 影响）
    /// </summary>
    /// <remarks>
    /// 设置后，MemoryContextProvider 在读取用户记忆的同时也会读取此 scope 的内容。
    /// 适用于全局业务知识、共享规则等所有用户都应看到的记忆。
    /// 为 null 时不读取全局记忆。save_memory 工具始终写入用户 scope，不写入全局 scope。
    /// </remarks>
    public string? SharedScope { get; set; }

    /// <summary>
    /// 是否启用用户级记忆隔离（按 UserId 隔离记忆）
    /// </summary>
    /// <remarks>
    /// 启用后，不同用户的记忆互不可见，这是多用户应用的安全默认值。
    /// 设为 false 时所有用户共享同一 scope（仅适用于单用户或全局知识库场景）。
    /// </remarks>
    public bool EnableUserIsolation { get; set; } = true;

    /// <summary>
    /// 是否启用自动记忆沉淀（对话结束时自动提取并持久化记忆）
    /// </summary>
    /// <remarks>
    /// 启用后，每次对话结束时会调用 LLM 从对话中提炼持久记忆条目。
    /// 默认关闭以避免额外的 LLM 调用成本。
    /// </remarks>
    public bool AutoPersist { get; set; }

    /// <summary>
    /// 自动沉淀使用的自定义提示词（null 时使用内置默认）
    /// </summary>
    public string? AutoPersistPrompt { get; set; }

    /// <summary>
    /// 自动沉淀的最大 token 数
    /// </summary>
    public int AutoPersistMaxTokens { get; set; } = 500;

    /// <summary>
    /// 沉淀时是否自动合并去重（仅在 AutoPersist=true 时生效）
    /// </summary>
    public bool AutoConsolidate { get; set; } = true;

    /// <summary>
    /// 当记忆条目数超过此阈值时触发合并
    /// </summary>
    public int ConsolidateThreshold { get; set; } = 30;

    /// <summary>
    /// 每个 scope 的最大条目数（null=不限制）
    /// </summary>
    /// <remarks>超过限制时自动删除最旧条目。</remarks>
    public int? MaxEntriesPerScope { get; set; }

    /// <summary>
    /// 条目过期时间（null=不过期）
    /// </summary>
    public TimeSpan? EntryExpiration { get; set; }

    /// <summary>
    /// 是否启用增量合并（Mem0 式 ADD/UPDATE/DELETE 决策）
    /// </summary>
    /// <remarks>
    /// 启用后，新记忆入库前会与已有记忆比对，由 IMemoryConsolidator 决定操作。
    /// 仅在 AutoPersist=true 时有意义。默认启用。
    /// </remarks>
    public bool IncrementalConsolidate { get; set; } = true;

    /// <summary>
    /// 增量合并时检索的相似记忆数
    /// </summary>
    public int ConsolidateSearchTopK { get; set; } = 3;

    /// <summary>
    /// 每次自动沉淀的最大合并调用次数（限制 LLM 成本）
    /// </summary>
    public int MaxConsolidationCallsPerPersist { get; set; } = 5;

    /// <summary>
    /// 重要性权重系数（用于搜索评分）
    /// </summary>
    public double ImportanceWeight { get; set; } = 1.0;

    /// <summary>
    /// 时间衰减速率（越大衰减越快，默认 0.01 ≈ 半衰期 70 天）
    /// </summary>
    public double RecencyDecayRate { get; set; } = 0.01;

    /// <summary>
    /// 允许的记忆类别（应用可扩展，如添加 "rule", "formula" 等）
    /// </summary>
    /// <remarks>
    /// 为空时不做类别校验。默认包含: preference, fact, decision, pattern, instruction。
    /// </remarks>
    public HashSet<string> ValidCategories { get; set; } =
        ["preference", "fact", "decision", "pattern", "instruction"];

    /// <summary>
    /// 是否启用 PII 防护（save_memory 时检测个人信息模式）
    /// </summary>
    public bool EnablePiiProtection { get; set; } = true;

    /// <summary>
    /// 记忆嵌入向量使用的 Provider（null=使用 AI:DefaultProvider）
    /// </summary>
    /// <remarks>
    /// 部分 Provider（如 DeepSeek）不支持 Embedding API，需要显式指定支持 Embedding 的 Provider。
    /// 推荐使用 OpenAI 的 text-embedding-3-small 或其他支持 Embedding 的模型。
    /// </remarks>
    public string? EmbeddingProvider { get; set; }

    /// <summary>
    /// 记忆嵌入向量使用的模型（null=使用 Provider 的 DefaultModel）
    /// </summary>
    public string? EmbeddingModel { get; set; }
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
    public int MaxResults { get; set; } = 10;

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

    /// <summary>
    /// 技能缓存 TTL（默认 15 分钟）
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// skill_search 工具的默认最大返回数
    /// </summary>
    public int SearchMaxResults { get; set; } = 10;
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
