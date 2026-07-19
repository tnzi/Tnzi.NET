
namespace Tnzi.Domain.Entities;

/// <summary>
/// 标记实体或属性不参与实体级审计采集（Tnzi.Audit 的 EntityAuditSaveChangesInterceptor）。
/// <para>
/// 属性级：该属性的 old/new 值不记录——适合令牌、密钥等"属性名太通用、
/// 无法进 AuditOptions.SensitiveFields 名单"的敏感值字段（如 AuthToken.Value，
/// 名单收录 "Value" 会误伤全部同名业务字段）。
/// 类级：整个实体类型不采集。
/// </para>
/// <para>
/// 与 SensitiveFields 的分工：名单按属性名跨实体掩码（记录"变了"但值打码，
/// 适合 PasswordHash 这类继承属性）；本特性按声明精确豁免（属性完全不进审计行）。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
