namespace Tnzi.AI.Options;

/// <summary>
/// LLM 提供商的基础配置 —— 发起一次对话补全所必需的最小字段集。
/// </summary>
/// <remarks>
/// ★本类刻意只包含「调用一次 LLM」所需的字段，不含 Agent 层概念
/// （降级链、thinking、prompt caching、上下文窗口等）。
/// 那些字段由 <c>Tnzi.AI</c> 程序集的派生类 <c>ProviderOptions</c> 补充，
/// 两者绑定同一个 <c>AI:Providers</c> 配置节的不同投影：
/// 核心只认基类字段，加载 AI 模块后由派生类认全部字段。
/// 配置文件是唯一事实源，两个视图都从它绑定，不存在同步问题。
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class AiProviderOptions
{
    /// <summary>
    /// 提供商名称 —— 由配置字典的键在 PostConfigure 阶段回填，配置文件中无需书写。
    /// </summary>
    /// <remarks>
    /// 用于把每个提供商路由到各自隔离的命名 HttpClient，
    /// 使某个提供商的 429/熔断不会波及其它提供商。
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用。未启用的提供商不能被解析，即使它是 DefaultProvider。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// API Key。留空时回退到环境变量 <c>AI__{PROVIDERNAME}__APIKEY</c>。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 基础 URL，需指向 OpenAI 兼容端点的版本前缀（如 <c>https://api.deepseek.com/v1</c>）。
    /// 留空时使用 <c>https://api.openai.com/v1</c>。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 默认模型名称。调用方未指定模型时使用。
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// 请求超时时间（秒）。未设置时使用 HttpClient 默认值。
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 最大输出 Token 数。
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 温度参数（0-2）。
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// 模型别名字典，如 <c>{ "think": "o4-mini", "fast": "gpt-4.1-mini" }</c>。
    /// </summary>
    /// <remarks>
    /// 请求中的模型名先在此字典中查找，命中则替换为实际模型名。
    /// </remarks>
    public Dictionary<string, string>? Models { get; set; }
}
