namespace Tnzi.AI.Entities;

/// <summary>
/// AI Provider 实体 - 数据库驱动的提供商定义
/// </summary>
/// <remarks>
/// 该实体是 <see cref="Tnzi.AI.Options.ProviderOptions"/> 的数据库覆盖/补充层。
/// 当数据库存在已启用的 Provider 记录时，<c>IChatClientFactory</c> 应优先使用数据库定义；
/// 数据库为空时回退到 appsettings.json 配置，保持向后兼容。
/// API Key 通过 <c>IDataProtectionProvider</c> 加密存储于 <see cref="ApiKeyEncrypted"/>。
/// </remarks>
public class Provider : FullAuditedEntity<Guid>, IScopedResource, IHasOrder
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
    /// Provider type - e.g. "OpenAI", "Anthropic", "Azure", "Ollama".
    /// 用作 IChatClientFactory 选择 SDK 的 key
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// Base URL override (nullable)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key ciphertext - 由 IDataProtectionProvider 加密。
    /// [AuditIgnore]：虽为密文，仍不进实体级审计（审计查看者与 DataProtection
    /// key ring 宿主可能是不同信任级，密文外泄面越小越好）。
    /// </summary>
    [AuditIgnore]
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>
    /// Default model name (nullable)
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Priority - 用于同类型多 Provider 排序
    /// </summary>
    /// <remarks>
    /// ★ 这是<b>运行时</b>取舍，不是展示顺序：同名多行时 <c>ChatClientFactory</c> 取
    /// Priority 最大的那一行去连。改它会换掉实际生效的 provider，所以拖拽排序
    /// <b>不</b>写这个字段——那是 <see cref="SortOrder"/> 的事。
    /// </remarks>
    public int Priority { get; set; }

    /// <summary>
    /// 展示顺序（管理端拖拽排序写入，值越小越靠前）
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Priority"/> 刻意分开：一个回答「列表怎么摆」，一个回答「实际连哪个」。
    /// 合成一个字段会让调整列表顺序悄悄改掉运行时行为。
    /// </remarks>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Description (nullable)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 可见性作用域 - System（全局共享）或 Tenant（租户私有）。
    /// 实体有意不实现 IMultiTenant，可见性通过服务层联合过滤（System ∪ 当前租户）强制。
    /// </summary>
    public ResourceScope Scope { get; set; } = ResourceScope.System;

    /// <summary>
    /// 所属租户 ID - Scope=Tenant 时非空；System 行为 null。
    /// </summary>
    public Guid? TenantId { get; set; }
}
