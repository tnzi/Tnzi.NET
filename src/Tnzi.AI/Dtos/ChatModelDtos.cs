namespace Tnzi.AI.Dtos;

/// <summary>
/// 模型清单里的一个模型（<c>AI:Chat:Models</c>）。
/// </summary>
public class ChatModelDto
{
    /// <summary>模型 id，聊天请求的 <c>model</c> 填它。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>属于哪个 provider；null = 哪个 Agent 都能用。</summary>
    public string? Provider { get; set; }

    /// <summary>一句说明；可能为 null。</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 这个模型可选的推理强度（运行时原生值，按显示次序）；空 = 这个模型不提供强度选择。聊天请求的 <c>effort</c> 填其中之一。
    /// </summary>
    /// <remarks>
    /// 取值依次看：管理员的策略（用户的单独设置 → 所有人的默认值）、模型清单（<c>AI:Chat:Models</c> 的 <c>Efforts</c>）、
    /// Agent 绑定的外部 CLI 运行时握手时报的档位（只在 <c>GET chat/models</c> 带了 Agent 时）。
    /// </remarks>
    public List<string> Efforts { get; set; } = [];

    /// <summary>默认选中的强度，是 <see cref="Efforts"/> 之一；null = 不预先选（这一轮不带强度时由绑定或运行时决定）。</summary>
    public string? DefaultEffort { get; set; }

    /// <summary>策略或模型清单给这个模型声明过档位（空列表 = 明确不提供）；没声明的才取运行时握手报的。</summary>
    [JsonIgnore]
    internal bool EffortsDeclared { get; set; }
}

/// <summary>
/// 管理员给一个模型设的可选强度（策略里按模型 id 存）。
/// </summary>
public class ChatModelEffortsDto
{
    /// <summary>可选的强度（运行时原生值，按显示次序）；空列表 = 这个模型不提供强度选择（握手报了档位也不提供）。</summary>
    public List<string> Efforts { get; set; } = [];

    /// <summary>默认选中的强度，必须是 <see cref="Efforts"/> 之一；null = 不预先选。</summary>
    [MaxLength(ChatModelEfforts.MaxLength)]
    public string? DefaultEffort { get; set; }
}

/// <summary>
/// 当前用户在某个 Agent 上可以选的模型。
/// </summary>
public class ChatModelsDto
{
    /// <summary>可以选的模型；多于一个时聊天界面显示选择器。</summary>
    public List<ChatModelDto> Models { get; set; } = [];

    /// <summary>默认选中的模型 id；没有可选模型时为 null。</summary>
    public string? DefaultModel { get; set; }
}
