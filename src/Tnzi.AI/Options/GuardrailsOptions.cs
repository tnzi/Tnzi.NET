namespace Tnzi.AI.Options;

/// <summary>
/// 安全防护 (Guardrails) 配置选项
/// </summary>
[ConfigSection("AI:Guardrails")]
[RuntimeSettingGroup(Key = "ai-guardrails", Module = "AI", DisplayName = "Guardrails",
    I18nKey = "admin.modules.system.settings.groups.aiGuardrails", Icon = "mdi:shield-check-outline", Order = 120)]
public class GuardrailsOptions
{
    /// <summary>
    /// 是否启用 Guardrails（默认关闭）
    /// </summary>
    [RuntimeSetting(Label = "Guardrails Enabled", I18n = "admin.modules.system.settings.fields.guardrailsEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Execution")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否启用输入长度限制
    /// </summary>
    [RuntimeSetting(Label = "Enable Max Length Check", I18n = "admin.modules.system.settings.fields.guardrailsEnableMaxLength",
        Type = SettingFieldType.Boolean, Subsection = "Detection")]
    public bool EnableMaxLength { get; set; } = true;

    /// <summary>
    /// 最大输入长度（字符数，默认 50000）
    /// </summary>
    [RuntimeSetting(Label = "Max Input Length", I18n = "admin.modules.system.settings.fields.guardrailsMaxInputLength",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Detection",
        Description = "Reject inputs longer than this many characters")]
    public int MaxInputLength { get; set; } = 50_000;

    /// <summary>
    /// 是否启用 Prompt 注入检测
    /// </summary>
    [RuntimeSetting(Label = "Prompt Injection Detection", I18n = "admin.modules.system.settings.fields.guardrailsEnablePromptInjectionDetection",
        Type = SettingFieldType.Boolean, Subsection = "Detection")]
    public bool EnablePromptInjectionDetection { get; set; } = true;

    /// <summary>
    /// 是否启用 PII 检测
    /// </summary>
    [RuntimeSetting(Label = "PII Detection", I18n = "admin.modules.system.settings.fields.guardrailsEnablePiiDetection",
        Type = SettingFieldType.Boolean, Subsection = "Detection")]
    public bool EnablePiiDetection { get; set; }

    /// <summary>
    /// 是否启用输出内容过滤
    /// </summary>
    [RuntimeSetting(Label = "Output Content Filter", I18n = "admin.modules.system.settings.fields.guardrailsEnableContentFilter",
        Type = SettingFieldType.Boolean, Subsection = "Detection")]
    public bool EnableContentFilter { get; set; }

    /// <summary>
    /// 输出内容过滤的屏蔽关键词列表
    /// </summary>
    public List<string> BlockedOutputKeywords { get; set; } = [];

    /// <summary>
    /// Guardrail 执行模式（默认顺序执行，支持并行执行 + Tripwire 立即中止）
    /// </summary>
    [RuntimeSetting(Label = "Execution Mode", I18n = "admin.modules.system.settings.fields.guardrailsExecutionMode",
        Type = SettingFieldType.Select, Subsection = "Execution",
        Description = "Sequential stops at the first rejection; Parallel runs all guardrails with tripwire abort")]
    public GuardrailExecutionMode ExecutionMode { get; set; } = GuardrailExecutionMode.Sequential;

    /// <summary>
    /// 流式输出 Guardrail 缓冲区大小（字符数，默认 500）
    /// </summary>
    /// <remarks>
    /// 流式场景下，每累积此数量的字符后执行一次输出 Guardrail 检查，
    /// 通过后才将缓冲的 chunk 释放给客户端。设为 0 禁用缓冲（退化为后验检查）。
    /// </remarks>
    [RuntimeSetting(Label = "Streaming Buffer Size", I18n = "admin.modules.system.settings.fields.guardrailsStreamingBufferSize",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Streaming",
        Description = "Characters buffered before each streaming output check (0 disables buffering)")]
    public int StreamingBufferSize { get; set; } = 500;

    /// <summary>
    /// 流式输出防护滑动窗口重叠大小（Token 数），用于检测跨窗口边界的违规关键词。
    /// 默认 50。
    /// </summary>
    [RuntimeSetting(Label = "Streaming Overlap Size", I18n = "admin.modules.system.settings.fields.guardrailsStreamingOverlapSize",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Streaming",
        Description = "Sliding-window overlap (tokens) to catch violations spanning buffer boundaries")]
    public int StreamingOverlapSize { get; set; } = 50;

    /// <summary>
    /// Fail-closed 模式 — 当 IGuardrailProvider 抛出异常时视为拒绝（默认 true）。
    /// 设为 false 时异常将被忽略（fail-open）。
    /// </summary>
    [RuntimeSetting(Label = "Fail Closed", I18n = "admin.modules.system.settings.fields.guardrailsFailClosed",
        Type = SettingFieldType.Boolean, Subsection = "Execution",
        Description = "Treat guardrail-provider exceptions as a rejection (fail-closed) instead of allowing through")]
    public bool FailClosed { get; set; } = true;

    /// <summary>
    /// 工具白名单/黑名单配置
    /// </summary>
    public AllowlistGuardrailOptions Allowlist { get; set; } = new();

    /// <summary>
    /// LLM-as-Judge guardrail 配置
    /// </summary>
    public LlmJudgeOptions LlmJudge { get; set; } = new();

    /// <summary>
    /// When true, ToolGuardrailMiddleware serializes tool arguments as Content
    /// and passes them to IGuardrailProvider content-inspection (PII / MaxLength etc.).
    /// Default false: only the tool-name allowlist/denylist runs, large legitimate
    /// payloads are not scanned and cannot inadvertently trip length or injection checks.
    /// </summary>
    [RuntimeSetting(Label = "Inspect Tool Arguments", I18n = "admin.modules.system.settings.fields.guardrailsInspectToolArguments",
        Type = SettingFieldType.Boolean, Subsection = "Detection",
        Description = "Also run content inspection (PII / length) over serialized tool-call arguments")]
    public bool InspectToolArguments { get; set; }
}

/// <summary>
/// Guardrail 执行模式
/// </summary>
public enum GuardrailExecutionMode
{
    /// <summary>
    /// 顺序执行，遇到第一个拒绝即停止
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行所有 guardrail，支持 tripwire 立即中止
    /// </summary>
    Parallel
}
