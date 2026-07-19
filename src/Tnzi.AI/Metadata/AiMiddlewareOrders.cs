namespace Tnzi.AI.Metadata;

/// <summary>
/// AI 中间件执行顺序常量 — 数值越小越先执行（共 23 个槽位）
/// </summary>
/// <remarks>
/// <para>
/// 完整管道（700 = Core Execution 位置，非中间件）：
///  50  ThreadData → 55 Sandbox → 60 FileUpload → 80 Thinking
/// → 100 Retry → 200 InputGuardrail → 250 Quota → 300 History → 350 Summarization
/// → 400 ContextInjection → 420 Todo → 450 SkillConstraint → 460 PromptCaching → 500 UsageLogging
/// → 550 SubAgentLimit → 630 ViewImage
/// → 650 LoopDetection → 655 ToolGuardrail → 660 ToolErrorRecovery
/// → [700 Core Execution]
/// → 800 OutputGuardrail → 900 Title → 950 Memory → 999 Clarification
/// </para>
/// </remarks>
public static class AiMiddlewareOrders
{
    // === Pre-execution: Environment Setup (50-80) ===

    /// <summary>线程数据目录隔离 (Phase 1)</summary>
    public const int ThreadData = 50;

    /// <summary>沙箱生命周期管理 (Phase 1)</summary>
    public const int Sandbox = 55;

    /// <summary>文件上传处理 (Phase 2)</summary>
    public const int FileUpload = 60;

    /// <summary>扩展思考注入 (Phase 2 enhanced)</summary>
    public const int Thinking = 80;

    // === Pre-execution: Protection & Input (100-200) ===

    /// <summary>重试与熔断保护</summary>
    public const int Retry = 100;

    /// <summary>输入安全防护 (Phase 5 enhanced)</summary>
    public const int InputGuardrail = 200;

    /// <summary>配额预检与预留</summary>
    public const int Quota = 250;

    // === Pre-execution: Context Assembly (300-500) ===

    /// <summary>对话历史加载 (Phase 1 enhanced: dangling fix)</summary>
    public const int History = 300;

    /// <summary>对话摘要 (Phase 2)</summary>
    public const int Summarization = 350;

    /// <summary>上下文注入：Memory/RAG/Skill/Soul/UserProfile/Template (Phase 3 enhanced)</summary>
    public const int ContextInjection = 400;

    /// <summary>Todo 任务管理</summary>
    public const int Todo = 420;

    /// <summary>技能约束执行：工具组/模型/Provider 过滤 (Phase 5 enhanced: audit)</summary>
    public const int SkillConstraint = 450;

    /// <summary>Prompt 缓存标记注入 — 在 ContextInjection + SkillConstraint 之后，可见全部消息和工具</summary>
    public const int PromptCaching = 460;

    /// <summary>用量日志记录 (Phase 3 enhanced: granular)</summary>
    public const int UsageLogging = 500;

    // === Pre-execution: Limits & Filters (550-660) ===

    /// <summary>子 Agent 数量限制 (Phase 1)</summary>
    public const int SubAgentLimit = 550;

    /// <summary>图片查看处理 (Phase 2)</summary>
    public const int ViewImage = 630;

    /// <summary>循环检测 (Phase 1)</summary>
    public const int LoopDetection = 650;

    /// <summary>工具执行安全防护 (Phase 5)</summary>
    public const int ToolGuardrail = 655;

    /// <summary>工具错误恢复 (Phase 1)</summary>
    public const int ToolErrorRecovery = 660;

    // === Post-execution: Output Processing (800-999) ===

    /// <summary>输出安全防护 (Phase 5 enhanced)</summary>
    public const int OutputGuardrail = 800;

    /// <summary>自动标题生成 — reserved: 当前由 AgentRuntime 内部处理，未独立为中间件</summary>
    public const int Title = 900;

    /// <summary>记忆存储 — reserved: 当前由 ContextInjectionMiddleware 的 OnCompleted 回调处理</summary>
    public const int Memory = 950;

    /// <summary>澄清中间件 — 必须最后执行 (Phase 3)</summary>
    public const int Clarification = 999;
}
