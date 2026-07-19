namespace Tnzi.Security;

/// <summary>
/// AES-256-GCM 认证加密辅助类(静态无状态)
/// </summary>
/// <remarks>
/// 密文格式:<c>v1:Base64(nonce(12 字节) + ciphertext + tag(16 字节))</c>。
/// 版本前缀为未来算法/格式轮换预留;GCM 提供机密性 + 完整性(篡改在解密时抛出
/// <see cref="CryptographicException"/>)。密钥为 256 位显式密钥(调用方负责保管,
/// 适合"密钥即配置、可跨环境迁移/备份恢复"的场景;与绑定宿主 key ring 的
/// ASP.NET Core Data Protection 互补)。
/// </remarks>
[StableApi(Since = "0.1.0")]
public static class AesGcmHelper
{
    private const int KeySizeBytes = 32;   // 256 位
    private const int NonceSizeBytes = 12; // GCM 标准 96 位 nonce
    private const int TagSizeBytes = 16;   // 128 位认证标签
    private const string VersionPrefix = "v1:";     // 无附加认证数据
    private const string VersionPrefixAad = "v2:";  // 绑定附加认证数据(AAD)

    /// <summary>
    /// 加密字符串,输出带版本前缀的 Base64 密文
    /// </summary>
    /// <param name="plaintext">明文(UTF-8 编码)</param>
    /// <param name="key">256 位(32 字节)密钥</param>
    public static string Encrypt(string plaintext, byte[] key)
        => Encrypt(plaintext, key, associatedData: null);

    /// <summary>
    /// 加密字符串并可选绑定附加认证数据(AAD)。AAD 不被加密但纳入 GCM 认证标签,
    /// 解密时必须提供**完全相同**的 AAD 否则失败——用于把密文绑定到其归属上下文
    /// (如 <c>租户:实体:归属键</c>),使密文无法被搬移到另一条记录复用。
    /// </summary>
    /// <param name="plaintext">明文(UTF-8 编码)</param>
    /// <param name="key">256 位(32 字节)密钥</param>
    /// <param name="associatedData">附加认证数据;null/空 = 退化为 v1 无 AAD 格式</param>
    public static string Encrypt(string plaintext, byte[] key, byte[]? associatedData)
    {
        Check.NotNull(plaintext);
        ValidateKey(key);

        var hasAad = associatedData is { Length: > 0 };
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var gcm = new AesGcm(key, TagSizeBytes);
        gcm.Encrypt(nonce, plainBytes, cipherBytes, tag, hasAad ? associatedData : null);

        var combined = new byte[NonceSizeBytes + cipherBytes.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSizeBytes, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes + cipherBytes.Length, TagSizeBytes);

        return (hasAad ? VersionPrefixAad : VersionPrefix) + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// 解密 <see cref="Encrypt(string, byte[])"/> 产出的密文
    /// </summary>
    /// <param name="protectedValue">带版本前缀的 Base64 密文</param>
    /// <param name="key">256 位(32 字节)密钥</param>
    /// <exception cref="FormatException">密文格式非法(前缀/长度/Base64)</exception>
    /// <exception cref="CryptographicException">密钥错误或密文被篡改</exception>
    public static string Decrypt(string protectedValue, byte[] key)
        => Decrypt(protectedValue, key, associatedData: null);

    /// <summary>
    /// 解密密文,对 v2(AAD 绑定)格式必须提供加密时相同的附加认证数据。
    /// v1 密文忽略 <paramref name="associatedData"/>(向后兼容存量无 AAD 数据)。
    /// AAD 不匹配 → 篡改视同,抛 <see cref="CryptographicException"/>。
    /// </summary>
    /// <param name="protectedValue">带版本前缀的 Base64 密文</param>
    /// <param name="key">256 位(32 字节)密钥</param>
    /// <param name="associatedData">v2 密文的附加认证数据(须与加密时一致);v1 忽略</param>
    /// <exception cref="FormatException">密文格式非法(前缀/长度/Base64)</exception>
    /// <exception cref="CryptographicException">密钥错误、AAD 不匹配或密文被篡改</exception>
    public static string Decrypt(string protectedValue, byte[] key, byte[]? associatedData)
    {
        Check.NotNullOrEmpty(protectedValue);
        ValidateKey(key);

        byte[]? aad;
        if (protectedValue.StartsWith(VersionPrefixAad, StringComparison.Ordinal))
            aad = associatedData is { Length: > 0 } ? associatedData : null;
        else if (protectedValue.StartsWith(VersionPrefix, StringComparison.Ordinal))
            aad = null; // v1: 无 AAD,忽略调用方传入值
        else
            throw new FormatException("Protected value does not carry a supported version prefix.");

        var combined = Convert.FromBase64String(protectedValue[VersionPrefix.Length..]);
        if (combined.Length < NonceSizeBytes + TagSizeBytes)
            throw new FormatException("Protected value is too short to contain nonce and tag.");

        var cipherLength = combined.Length - NonceSizeBytes - TagSizeBytes;
        var nonce = new byte[NonceSizeBytes];
        var cipherBytes = new byte[cipherLength];
        var tag = new byte[TagSizeBytes];
        Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(combined, NonceSizeBytes, cipherBytes, 0, cipherLength);
        Buffer.BlockCopy(combined, NonceSizeBytes + cipherLength, tag, 0, TagSizeBytes);

        var plainBytes = new byte[cipherLength];
        using var gcm = new AesGcm(key, TagSizeBytes);
        gcm.Decrypt(nonce, cipherBytes, tag, plainBytes, aad);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 判断字符串是否为本类产出的密文(带受支持的版本前缀)
    /// </summary>
    public static bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value)
            && (value.StartsWith(VersionPrefix, StringComparison.Ordinal)
                || value.StartsWith(VersionPrefixAad, StringComparison.Ordinal));

    /// <summary>
    /// 生成随机 256 位密钥的 Base64 表示(供运维生成配置值)
    /// </summary>
    public static string GenerateKeyBase64()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeBytes));

    private static void ValidateKey(byte[] key)
    {
        Check.NotNull(key);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes (256-bit).", nameof(key));
    }
}
