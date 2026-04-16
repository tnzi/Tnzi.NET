namespace Tnzi.AI.Entities;

/// <summary>
/// AI Provider 实体 — 数据库驱动的提供商定义
/// </summary>
/// <remarks>
/// 该实体是 <see cref="Tnzi.AI.Options.ProviderOptions"/> 的数据库覆盖/补充层。
/// 当数据库存在已启用的 Provider 记录时，<c>IChatClientFactory</c> 应优先使用数据库定义；
/// 数据库为空时回退到 appsettings.json 配置，保持向后兼容。
/// API Key 通过 <c>IDataProtectionProvider</c> 加密存储于 <see cref="ApiKeyEncrypted"/>。
/// </remarks>
public class Provider : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Single source of truth for the IDataProtectionProvider purpose string used to
    /// encrypt <see cref="ApiKeyEncrypted"/>. ProviderService (writer) and
    /// ChatClientFactory (reader) both reference this constant so a rename cannot
    /// silently desynchronize the two sides.
    /// </summary>
    public const string ApiKeyProtectorPurpose = "Tnzi.AI.Providers.ApiKey";

    /// <summary>
    /// Display name (unique among non-deleted rows)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Provider type — e.g. "OpenAI", "Anthropic", "Azure", "Ollama".
    /// 用作 IChatClientFactory 选择 SDK 的 key
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// Base URL override (nullable)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key ciphertext — 由 IDataProtectionProvider 加密
    /// </summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>
    /// Default model name (nullable)
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Priority — 用于同类型多 Provider 排序
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Description (nullable)
    /// </summary>
    public string? Description { get; set; }
}
