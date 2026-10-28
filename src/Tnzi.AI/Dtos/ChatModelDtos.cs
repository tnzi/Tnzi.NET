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
