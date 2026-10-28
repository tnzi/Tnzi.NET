using System.Security.Cryptography;
using Tnzi.Security;

namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 信封加密的字段级加密器:每个值用一把新生成的随机数据密钥加密,数据密钥再被密钥环里的主密钥封装,封装结果随密文存放。
/// </summary>
/// <remarks>
/// <para>
/// <strong>密文形态:</strong><c>{keyId}:e1:{封装的数据密钥}:{载荷}</c>。<c>keyId</c> 指向封装数据密钥的主密钥;
/// 封装的数据密钥与载荷都是核心 <see cref="AesGcmHelper"/> 的 v2 输出去掉 <c>v2:</c> 前缀后的 Base64
/// (去掉前缀是为了让列宽可精确计算,且段内不含冒号)。
/// </para>
/// <para>
/// <strong>两处都绑 purpose。</strong>载荷与封装的数据密钥各自把 purpose 作为 AAD(封装用的 AAD 另加固定前缀,
/// 两处不能互换):A 列的密文或封装密钥搬到 B 列都解不开;同一列里把一个值的封装密钥换到另一个值上,
/// 载荷认证标签对不上,同样失败。
/// </para>
/// <para>
/// <strong>主密钥轮换只需重新封装数据密钥。</strong>见 <see cref="IFieldRewrapper"/>:载荷一个字节都不动。
/// </para>
/// <para>
/// <strong>兼容直接模式的存量密文。</strong>从 <see cref="FieldEncryptionMode.Direct"/> 切到信封模式后,
/// 旧格式的值仍能读取(<see cref="IsEncrypted"/> 对两种格式都为真,否则旧密文会被当成明文原样放行),
/// 并被 <see cref="NeedsRewrap"/> 标为待转换。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "字段级加密的信封格式与重新封装流程仍在演进,0.2 前可能调整密文布局")]
public sealed class EnvelopeFieldEncryptor : IFieldEncryptor, IFieldRewrapper
{
    private const string EnvelopeMarker = "e1";
    private const string WrapAadPrefix = "tnzi-field-dek|";
    private const string AadVersionPrefix = "v2:";
    private const int DataKeySizeBytes = 32;

    /// <summary>
    /// 密文里除载荷 Base64 以外的最大字符数:密钥标识上限 + <c>:e1:</c> + 封装的数据密钥(96 个字符)+ 分隔冒号。
    /// 封装的是 44 个字符的 Base64 数据密钥,加 28 字节随机数与标签共 72 字节,Base64 后 96 个字符。
    /// </summary>
    public const int OverheadChars = FieldEncryptionOptions.MaxKeyIdLength + 4 + 96 + 1;

    private readonly FieldEncryptionKeyRing _keyRing;
    private readonly AesGcmFieldEncryptor _direct;

    /// <summary>
    /// 用共享的密钥环初始化。
    /// </summary>
    public EnvelopeFieldEncryptor(FieldEncryptionKeyRing keyRing)
    {
        _keyRing = Check.NotNull(keyRing);
        _direct = new AesGcmFieldEncryptor(keyRing);
    }

    /// <inheritdoc />
    public int CiphertextOverheadChars => OverheadChars;

    /// <inheritdoc />
    public string Encrypt(string plaintext, string purpose)
    {
        Check.NotNull(plaintext);
        Check.NotNullOrWhiteSpace(purpose);

        if (!_keyRing.TryGetActive(out var keyId, out var masterKey))
        {
            throw AesGcmFieldEncryptor.NoActiveKey();
        }

        var dataKey = RandomNumberGenerator.GetBytes(DataKeySizeBytes);
        try
        {
            var payload = AesGcmHelper.Encrypt(plaintext, dataKey, Encoding.UTF8.GetBytes(purpose));
            var wrapped = WrapDataKey(dataKey, masterKey, purpose);
            return Compose(keyId, wrapped, payload);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new FieldEncryptionException($"Failed to encrypt field '{purpose}' with key '{keyId}'.", isKeyMissing: false, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext, string purpose)
    {
        Check.NotNullOrWhiteSpace(ciphertext);
        Check.NotNullOrWhiteSpace(purpose);

        if (!TryParse(ciphertext, out var parts))
        {
            return _direct.Decrypt(ciphertext, purpose);
        }

        if (!_keyRing.TryGetKey(parts.KeyId, out var masterKey))
        {
            throw AesGcmFieldEncryptor.KeyNotInRing(purpose, parts.KeyId);
        }

        byte[]? dataKey = null;
        try
        {
            dataKey = UnwrapDataKey(parts.Wrapped, masterKey, purpose);
            return AesGcmHelper.Decrypt(AadVersionPrefix + parts.Payload, dataKey, Encoding.UTF8.GetBytes(purpose));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new FieldEncryptionException(
                $"Failed to decrypt field '{purpose}' with key '{parts.KeyId}'. "
                + "The value or its wrapped data key may have been tampered with, or copied from a different column.",
                isKeyMissing: false,
                ex);
        }
        finally
        {
            if (dataKey is not null)
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
    }

    /// <inheritdoc />
    public bool IsEncrypted(string value)
        => !string.IsNullOrEmpty(value) && (TryParse(value, out _) || _direct.IsEncrypted(value));

    /// <inheritdoc />
    public bool NeedsRewrap(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return false;
        }

        if (TryParse(ciphertext, out var parts))
        {
            return !string.Equals(parts.KeyId, _keyRing.ActiveKeyId, StringComparison.Ordinal);
        }

        // 直接模式的存量密文:转换成信封格式。
        return _direct.IsEncrypted(ciphertext);
    }

    /// <inheritdoc />
    public string Rewrap(string ciphertext, string purpose)
    {
        Check.NotNullOrWhiteSpace(ciphertext);
        Check.NotNullOrWhiteSpace(purpose);

        if (!TryParse(ciphertext, out var parts))
        {
            // 直接模式的值没有可单独换封的数据密钥,只能解开后按信封格式重新加密。
            return Encrypt(_direct.Decrypt(ciphertext, purpose), purpose);
        }

        if (!_keyRing.TryGetActive(out var activeKeyId, out var activeKey))
        {
            throw AesGcmFieldEncryptor.NoActiveKey();
        }

        if (string.Equals(parts.KeyId, activeKeyId, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        if (!_keyRing.TryGetKey(parts.KeyId, out var oldKey))
        {
            throw AesGcmFieldEncryptor.KeyNotInRing(purpose, parts.KeyId);
        }

        byte[]? dataKey = null;
        try
        {
            dataKey = UnwrapDataKey(parts.Wrapped, oldKey, purpose);
            var rewrapped = WrapDataKey(dataKey, activeKey, purpose);
            return Compose(activeKeyId, rewrapped, AadVersionPrefix + parts.Payload);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new FieldEncryptionException(
                $"Failed to re-wrap field '{purpose}' from key '{parts.KeyId}'. The wrapped data key may have been tampered with, or copied from a different column.",
                isKeyMissing: false,
                ex);
        }
        finally
        {
            if (dataKey is not null)
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
    }

    private static string WrapDataKey(byte[] dataKey, byte[] masterKey, string purpose)
        => AesGcmHelper.Encrypt(Convert.ToBase64String(dataKey), masterKey, Encoding.UTF8.GetBytes(WrapAadPrefix + purpose));

    private static byte[] UnwrapDataKey(string wrappedBase64, byte[] masterKey, string purpose)
    {
        var encoded = AesGcmHelper.Decrypt(AadVersionPrefix + wrappedBase64, masterKey, Encoding.UTF8.GetBytes(WrapAadPrefix + purpose));
        var dataKey = Convert.FromBase64String(encoded);
        if (dataKey.Length != DataKeySizeBytes)
        {
            throw new FormatException("Unwrapped data key has an unexpected length.");
        }

        return dataKey;
    }

    /// <summary>
    /// 拼出密文。<paramref name="wrapped"/> 与 <paramref name="payload"/> 都是带 <c>v2:</c> 前缀的助手输出。
    /// </summary>
    private static string Compose(string keyId, string wrapped, string payload)
        => string.Concat(keyId, ":", EnvelopeMarker, ":", wrapped[AadVersionPrefix.Length..], ":", payload[AadVersionPrefix.Length..]);

    private static bool TryParse(string value, out EnvelopeParts parts)
    {
        parts = default;

        var segments = value.Split(':');
        if (segments.Length != 4
            || segments[0].Length == 0
            || segments[1] != EnvelopeMarker
            || segments[2].Length == 0
            || segments[3].Length == 0)
        {
            return false;
        }

        parts = new EnvelopeParts(segments[0], segments[2], segments[3]);
        return true;
    }

    private readonly record struct EnvelopeParts(string KeyId, string Wrapped, string Payload);
}
