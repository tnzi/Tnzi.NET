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

/// <summary>
/// Provider 实体 DTO（输出形状）— 永不暴露明文或密文 API Key
/// </summary>
public class ProviderDto
{
    /// <summary>Provider id</summary>
    public Guid Id { get; set; }

    /// <summary>Display name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider type — e.g. OpenAI / Anthropic / Azure / Ollama</summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Base URL override</summary>
    public string? Endpoint { get; set; }

    /// <summary>Default model name</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Priority — for ordering when multiple providers of same type exist</summary>
    public int Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Whether an API key is configured (true = ciphertext present)</summary>
    public bool HasApiKey { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// Provider 分页查询 DTO
/// </summary>
public class ProviderQueryDto : PagedQueryDto
{
    /// <summary>按 ProviderType 过滤（可选）</summary>
    public string? ProviderType { get; set; }

    /// <summary>仅返回启用项（可选）</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>关键字过滤（匹配 Name / Description）</summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// Provider 创建请求 DTO
/// </summary>
public class CreateProviderDto
{
    /// <summary>Display name</summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider type — e.g. OpenAI / Anthropic / Azure / Ollama</summary>
    [Required]
    [StringLength(50)]
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Base URL override</summary>
    [StringLength(500)]
    public string? Endpoint { get; set; }

    /// <summary>API key (plaintext) — encrypted at rest</summary>
    public string? ApiKey { get; set; }

    /// <summary>Default model name</summary>
    [StringLength(100)]
    public string? DefaultModel { get; set; }

    /// <summary>Priority</summary>
    public int Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Description</summary>
    [StringLength(500)]
    public string? Description { get; set; }
}

/// <summary>
/// Provider 更新请求 DTO — 字段可选；ApiKey 为 null 时保留现有密文
/// </summary>
public class UpdateProviderDto
{
    /// <summary>Display name</summary>
    [StringLength(100)]
    public string? Name { get; set; }

    /// <summary>Provider type</summary>
    [StringLength(50)]
    public string? ProviderType { get; set; }

    /// <summary>Base URL override</summary>
    [StringLength(500)]
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key (plaintext). null = 保持现有；空字符串 = 清除密钥；非空 = 加密替换
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Default model name</summary>
    [StringLength(100)]
    public string? DefaultModel { get; set; }

    /// <summary>Priority</summary>
    public int? Priority { get; set; }

    /// <summary>Whether enabled</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Description</summary>
    [StringLength(500)]
    public string? Description { get; set; }
}

/// <summary>
/// Provider 连接测试结果 DTO
/// </summary>
public class ProviderTestResultDto
{
    /// <summary>Whether the probe succeeded</summary>
    public bool Success { get; set; }

    /// <summary>Optional message (error detail or status)</summary>
    public string? Message { get; set; }

    /// <summary>Probe latency in milliseconds</summary>
    public long LatencyMs { get; set; }
}
