using System.Security.Cryptography;
using Tnzi.Security;

namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 基于核心 <see cref="AesGcmHelper"/> 的字段级加密器(直接模式),密钥来自配置密钥环与密钥文件。
/// </summary>
/// <remarks>
/// <para>
/// <strong>密文形态:</strong><c>{keyId}:{AesGcmHelper 输出}</c>,
/// 例如 <c>k1:v2:BASE64...</c>。外层的 <c>keyId</c> 让解密能找回当初那把密钥(支持轮换),
/// 内层由核心助手负责认证加密,并把 purpose 作为附加认证数据(AAD)绑进密文。
/// </para>
/// <para>
/// <strong>为什么把 purpose 绑进 AAD。</strong>没有它,一段密文可以从 A 列整段复制到 B 列,
/// 数据库层看不出异常,解密也会成功,攻击者不需要读懂内容就能把「某人的备注」
/// 换成「另一个人的备注」。绑定之后,换了列就解不开。
/// </para>
/// <para>
/// <strong>本实现把密钥留在进程内存里。</strong>每个值都直接用密钥环里的密钥加密;
/// 要「每个值一把数据密钥、主密钥只封装数据密钥」,用 <see cref="EnvelopeFieldEncryptor"/>。
/// 要对接 KMS/HSM,替换 <see cref="IFieldEncryptor"/> 即可。
/// </para>
/// </remarks>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private readonly FieldEncryptionKeyRing _keyRing;

    /// <summary>
    /// 用配置选项初始化,密钥环由本实例自建。
    /// </summary>
    /// <param name="options">
    /// 用 <see cref="IOptionsMonitor{TOptions}"/> 而非 <c>IOptions</c>,
    /// 以便密钥轮换后无需重启即可生效(本类注册为单例)。
    /// </param>
    public AesGcmFieldEncryptor(IOptionsMonitor<FieldEncryptionOptions> options)
        : this(new FieldEncryptionKeyRing(Check.NotNull(options)))
    {
    }

    /// <summary>
    /// 用共享的密钥环初始化。
    /// </summary>
    public AesGcmFieldEncryptor(FieldEncryptionKeyRing keyRing)
    {
        _keyRing = Check.NotNull(keyRing);
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext, string purpose)
    {
        Check.NotNull(plaintext);
        Check.NotNullOrWhiteSpace(purpose);

        if (!_keyRing.TryGetActive(out var keyId, out var key))
        {
            throw NoActiveKey();
        }

        try
        {
            var payload = AesGcmHelper.Encrypt(plaintext, key, Encoding.UTF8.GetBytes(purpose));
            return string.Concat(keyId, ":", payload);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // 不把明文或密钥材料带进异常消息。
            throw new FieldEncryptionException(
                $"Failed to encrypt field '{purpose}' with key '{keyId}'.",
                isKeyMissing: false,
                ex);
        }
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext, string purpose)
    {
        Check.NotNullOrWhiteSpace(ciphertext);
        Check.NotNullOrWhiteSpace(purpose);

        if (!TrySplit(ciphertext, out var keyId, out var payload))
        {
            throw new FieldEncryptionException(
                $"Value of field '{purpose}' is not a well-formed encrypted value.");
        }

        if (!_keyRing.TryGetKey(keyId, out var key))
        {
            throw KeyNotInRing(purpose, keyId);
        }

        try
        {
            return AesGcmHelper.Decrypt(payload, key, Encoding.UTF8.GetBytes(purpose));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new FieldEncryptionException(
                $"Failed to decrypt field '{purpose}' with key '{keyId}'. "
                + "The value may have been tampered with, or copied from a different column.",
                isKeyMissing: false,
                ex);
        }
    }

    /// <inheritdoc />
    public bool IsEncrypted(string value)
        => !string.IsNullOrEmpty(value)
           && TrySplit(value, out _, out var payload)
           && AesGcmHelper.IsProtected(payload);

    internal static FieldEncryptionException NoActiveKey() => new(
        "No active field-encryption key is configured. Set EFCore:FieldEncryption:ActiveKeyId (or activeKeyId in the key file) to a key present in the key ring.",
        isKeyMissing: true);

    internal static FieldEncryptionException KeyNotInRing(string purpose, string keyId) => new(
        // 密钥不在环里 = 这批数据已被加密删除,或运维漏配了历史密钥。
        // 两种情况都必须报错:静默返回空值会让「已销毁」看起来像「本来就没填」。
        $"Field '{purpose}' was encrypted with key '{keyId}', which is not present in the current key ring. "
        + "The data is unreadable by design if that key was destroyed, or the key is missing from configuration.",
        isKeyMissing: true);

    /// <summary>
    /// 从 <c>{keyId}:{payload}</c> 中拆出密钥标识与载荷。
    /// </summary>
    /// <remarks>
    /// 只按<strong>第一个</strong>冒号拆分:载荷本身带 <c>v1:</c> / <c>v2:</c> 前缀,
    /// 里面还有冒号。密钥标识不允许含冒号(由选项验证器保证)。
    /// </remarks>
    internal static bool TrySplit(string value, out string keyId, out string payload)
    {
        keyId = string.Empty;
        payload = string.Empty;

        var index = value.IndexOf(':');
        if (index <= 0 || index == value.Length - 1)
        {
            return false;
        }

        keyId = value[..index];
        payload = value[(index + 1)..];
        return true;
    }
}
