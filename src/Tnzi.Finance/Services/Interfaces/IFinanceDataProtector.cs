namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 财务敏感字段加密器（银行账号等 remit-to / EFT 明文的对称加密封装）
/// </summary>
/// <remarks>
/// 底层走核心 <see cref="Security.AesGcmHelper"/>（AES-256-GCM，<c>v1:</c> 版本前缀），
/// 密钥取自 <see cref="Options.FinanceEncryptionOptions"/>。未配置密钥时
/// <see cref="IsConfigured"/> 为 false，写加密字段的服务层应据此返回 400 引导。
/// </remarks>
public interface IFinanceDataProtector
{
    /// <summary>是否已配置加密密钥</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// 加密明文，返回带版本前缀的 Base64 密文。
    /// </summary>
    /// <exception cref="Exceptions.BusinessException">未配置密钥（400）</exception>
    string Protect(string plaintext);

    /// <summary>
    /// 加密明文并把密文绑定到归属上下文 <paramref name="associatedData"/>（AAD）。
    /// 解密时须提供相同的 AAD，否则失败——使密文无法被搬移到另一条记录复用
    /// （如把甲账户的加密账号搬到乙账户）。存量无 AAD（v1）密文仍可用不带 AAD 的重载解密。
    /// </summary>
    /// <exception cref="Exceptions.BusinessException">未配置密钥（400）</exception>
    string Protect(string plaintext, string associatedData);

    /// <summary>
    /// 解密密文，返回明文。
    /// </summary>
    /// <exception cref="Exceptions.BusinessException">未配置密钥（400）</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">密钥错误或密文被篡改</exception>
    string Unprotect(string protectedValue);

    /// <summary>
    /// 解密密文；对 AAD 绑定（v2）密文须提供加密时相同的 <paramref name="associatedData"/>。
    /// v1 密文忽略该参数（向后兼容存量）。AAD 不匹配视同篡改。
    /// </summary>
    /// <exception cref="Exceptions.BusinessException">未配置密钥（400）</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">密钥错误、AAD 不匹配或密文被篡改</exception>
    string Unprotect(string protectedValue, string associatedData);
}
