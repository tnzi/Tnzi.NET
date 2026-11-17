namespace Tnzi.Identity.OpenIdConnect.Entities;

/// <summary>
/// 一个登记在本提供方的 OpenID Connect 客户端（依赖方应用）。
/// </summary>
/// <remarks>
/// <para>
/// 协议字段（client_id、回调地址、权限、要求等）继承自 OpenIddict 的实体，由 OpenIddict 的管理器读写，
/// 本类只补框架需要的三样：启停开关、给消费方挂自己记录的外部引用、创建时间。
/// </para>
/// <para>
/// 子类化而不是直接用 OpenIddict 的默认实体，是为了让它进本程序集：表名前缀（<c>Identity_</c>）、
/// 框架的主键生成与实体自动注册都按程序集归属生效，外来实体一个都拿不到。
/// </para>
/// </remarks>
public class OidcClient : OpenIddictEntityFrameworkCoreApplication<Guid, OidcAuthorization, OidcToken>, IEntity<Guid>, IHasCreationTime
{
    /// <summary>外部引用的最大长度。</summary>
    public const int ExternalReferenceMaxLength = 200;

    /// <summary>说明的最大长度。</summary>
    public const int DescriptionMaxLength = 1000;

    /// <summary>
    /// 客户端密钥（OpenIddict 存的是哈希）。不进实体审计：哈希也不该散落到审计表里。
    /// </summary>
    [AuditIgnore]
    public override string? ClientSecret { get => base.ClientSecret; set => base.ClientSecret = value; }

    /// <summary>
    /// 是否启用。停用时 authorize 与令牌端点都拒绝它，已签出的刷新令牌随之失效。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 消费方自己的记录标识（例如「托管应用 Id」），用于「一个应用一个客户端」的反查。可空，不要求唯一。
    /// </summary>
    public string? ExternalReference { get; set; }

    /// <summary>说明（管理端展示用）。</summary>
    public string? Description { get; set; }

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public object[] GetKeys() => [Id];
}
