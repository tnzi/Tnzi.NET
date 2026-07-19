namespace Tnzi.Finance.Options;

/// <summary>
/// 财务模块敏感数据加密配置（银行账号等 remit-to / EFT 明文字段）
/// </summary>
/// <remarks>
/// 密钥即配置：显式的 Base64 编码 256 位密钥（32 字节），可跨环境迁移、随备份恢复
/// （与绑定宿主 key ring 的 ASP.NET Core Data Protection 互补）。
///
/// **不作为运行时热设置**：热改密钥会使全部存量密文无法解密（GCM 认证失败），
/// 因此本项仅作启动配置，通过 <c>Finance:Encryption:EncryptionKey</c> 提供。
/// 未配置时，写入需要加密的银行明细将被拒绝（400），读取不受影响。
/// </remarks>
[ConfigSection("Finance:Encryption")]
public class FinanceEncryptionOptions
{
    /// <summary>
    /// Base64 编码的 256 位（32 字节）加密密钥。留空表示未启用银行明细加密。
    /// </summary>
    /// <remarks>运维可用 <c>AesGcmHelper.GenerateKeyBase64()</c> 生成一个新密钥。</remarks>
    public string? EncryptionKey { get; set; }
}
