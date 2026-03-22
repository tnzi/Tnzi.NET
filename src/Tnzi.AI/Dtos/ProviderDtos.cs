namespace Tnzi.AI.Dtos;

/// <summary>
/// Provider 默认模型信息 DTO
/// </summary>
public class ProviderDefaultModelDto
{
    /// <summary>Provider name</summary>
    public string ProviderName { get; set; } = string.Empty;
    /// <summary>Default model name</summary>
    public string? DefaultModel { get; set; }
}
