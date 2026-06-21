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
    /// <summary>
    /// Provider id。数据库条目为实体主键；配置条目为按名称派生的确定性合成 id
    /// （仅用于前端 rowKey/详情查询，对其执行写操作会返回 400）。
    /// </summary>
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

    /// <summary>Visibility scope — System (shared) or Tenant (private to tenant)</summary>
    public ResourceScope Scope { get; set; }

    /// <summary>Tenant ID — populated when Scope=Tenant</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 来源 — <see cref="ProviderSources.Database"/>（实体，可写）或
    /// <see cref="ProviderSources.Configuration"/>（appsettings AI:Providers，只读）。
    /// </summary>
    public string Source { get; set; } = ProviderSources.Database;

    /// <summary>Creation time — 配置来源条目无创建时间，为 null</summary>
    public DateTime? CreationTime { get; set; }

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
    public string Name { get; set; } = null!;

    /// <summary>Provider type — e.g. OpenAI / Anthropic / Azure / Ollama</summary>
    [Required]
    [StringLength(50)]
    public string ProviderType { get; set; } = null!;

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

    /// <summary>
    /// Visibility scope — defaults to Tenant when current tenant context is active, System otherwise.
    /// Callers may override explicitly.
    /// </summary>
    public ResourceScope? Scope { get; set; }
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
/// Provider 下拉选项 DTO — 给前端 Agent 配置的 Provider 下拉框使用（轻量，仅启用项）
/// </summary>
public class ProviderOptionDto
{
    /// <summary>Provider id</summary>
    public Guid Id { get; set; }
    /// <summary>Display name (Agent.Provider 存的就是这个 Name)</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Provider type — OpenAI / Anthropic / Azure / Ollama / ...</summary>
    public string ProviderType { get; set; } = string.Empty;
    /// <summary>Default model name (用作 Model 下拉的默认选项)</summary>
    public string? DefaultModel { get; set; }
    /// <summary>来源 — Database（实体）或 Configuration（appsettings AI:Providers）</summary>
    public string Source { get; set; } = ProviderSources.Database;
}

/// <summary>
/// Provider 模型列表 DTO — 来自第三方 /v1/models 实时查询，失败时按 ProviderType 静态兜底
/// </summary>
public class ProviderModelsDto
{
    /// <summary>Model id 列表</summary>
    public List<string> Models { get; set; } = [];
    /// <summary>数据来源：live = /v1/models 实时；fallback = 静态兜底列表</summary>
    public string Source { get; set; } = "fallback";
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
