namespace Tnzi.Finance.Services;

/// <summary>
/// 财务敏感字段加密器（基于核心 <see cref="AesGcmHelper"/> 与显式配置密钥）
/// </summary>
/// <remarks>
/// 密钥非热配（<see cref="FinanceEncryptionOptions"/> 无 [RuntimeSetting]），构造期解码一次并缓存。
/// 未配置时 <see cref="IsConfigured"/> 为 false，加解密调用抛 400 业务异常。
/// </remarks>
public class FinanceDataProtector : IFinanceDataProtector
{
    private const string NotConfiguredMessage =
        "Configure Finance:Encryption:EncryptionKey before storing bank details.";

    private readonly byte[]? _key;

    public FinanceDataProtector(IOptions<FinanceEncryptionOptions> options)
    {
        var configured = Check.NotNull(options).Value.EncryptionKey;
        _key = string.IsNullOrWhiteSpace(configured) ? null : Convert.FromBase64String(configured);
    }

    public bool IsConfigured => _key != null;

    public string Protect(string plaintext)
    {
        Check.NotNull(plaintext);
        if (_key == null)
            throw new BusinessException(NotConfiguredMessage);
        return AesGcmHelper.Encrypt(plaintext, _key);
    }

    public string Protect(string plaintext, string associatedData)
    {
        Check.NotNull(plaintext);
        Check.NotNullOrWhiteSpace(associatedData);
        if (_key == null)
            throw new BusinessException(NotConfiguredMessage);
        return AesGcmHelper.Encrypt(plaintext, _key, Encoding.UTF8.GetBytes(associatedData));
    }

    public string Unprotect(string protectedValue)
    {
        Check.NotNullOrEmpty(protectedValue);
        if (_key == null)
            throw new BusinessException(NotConfiguredMessage);
        return AesGcmHelper.Decrypt(protectedValue, _key);
    }

    public string Unprotect(string protectedValue, string associatedData)
    {
        Check.NotNullOrEmpty(protectedValue);
        Check.NotNullOrWhiteSpace(associatedData);
        if (_key == null)
            throw new BusinessException(NotConfiguredMessage);
        return AesGcmHelper.Decrypt(protectedValue, _key, Encoding.UTF8.GetBytes(associatedData));
    }
}
