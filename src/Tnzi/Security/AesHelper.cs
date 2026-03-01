
namespace Tnzi.Security;

/// <summary>
/// AES加密解密辅助类
/// </summary>
public class AesHelper
{
    private readonly bool _needIV;
    private const int KeySize = 32; // 256 bits
    private const int IVSize = 16;  // 128 bits

    /// <summary>
    /// 初始化一个<see cref="AesHelper"/>类型的新实例
    /// </summary>
    public AesHelper(bool needIV = true)
        : this(GenerateRandomKey(), needIV)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="AesHelper"/>类型的新实例
    /// </summary>
    /// <param name="key">加密密钥（32字节，256位）</param>
    /// <param name="needIV">是否需要随机初始化向量（默认 true，推荐开启以确保语义安全性）</param>
    public AesHelper(string key, bool needIV = true)
    {
        Check.NotNullOrEmpty(key);

        Key = key;
        _needIV = needIV;
    }

    /// <summary>
    /// 获取 加密密钥
    /// </summary>
    public string Key { get; }

    #region 实例方法

    /// <summary>
    /// 加密字节数组
    /// </summary>
    public byte[] Encrypt(byte[] decodeBytes)
    {
        return Encrypt(decodeBytes, Key, _needIV);
    }

    /// <summary>
    /// 解密字节数组
    /// </summary>
    public byte[] Decrypt(byte[] encodeBytes)
    {
        return Decrypt(encodeBytes, Key, _needIV);
    }

    /// <summary>
    /// 加密字符串, 输出为Base64编码的字符串
    /// </summary>
    public string Encrypt(string source)
    {
        return Encrypt(source, Key, _needIV);
    }

    /// <summary>
    /// 解密字符串, 输入为Base64编码的字符串
    /// </summary>
    public string Decrypt(string source)
    {
        return Decrypt(source, Key, _needIV);
    }

    #endregion

    #region 静态方法

    /// <summary>
    /// 加密字节数组
    /// </summary>
    public static byte[] Encrypt(byte[] decodeBytes, string key, bool needIV = true)
    {
        Check.NotNullOrEmpty(decodeBytes);
        Check.NotNullOrEmpty(key);

        byte[] keyBytes = GetKeyBytes(key);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        if (needIV)
        {
            aes.GenerateIV();
        }
        else
        {
            aes.IV = new byte[IVSize]; // 全零IV（不推荐，但兼容旧代码）
        }

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        
        if (needIV)
        {
            ms.Write(aes.IV, 0, aes.IV.Length);
        }

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(decodeBytes, 0, decodeBytes.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 解密字节数组
    /// </summary>
    public static byte[] Decrypt(byte[] encodeBytes, string key, bool needIV = true)
    {
        Check.NotNullOrEmpty(encodeBytes);
        Check.NotNullOrEmpty(key);

        byte[] keyBytes = GetKeyBytes(key);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream(encodeBytes);
        
        if (needIV)
        {
            byte[] iv = new byte[IVSize];
            ms.ReadExactly(iv, 0, IVSize);
            aes.IV = iv;
        }
        else
        {
            aes.IV = new byte[IVSize];
        }

        using var decryptor = aes.CreateDecryptor();
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var result = new MemoryStream();
        
        cs.CopyTo(result);
        return result.ToArray();
    }

    /// <summary>
    /// 加密字符串, 输出为Base64编码的字符串
    /// </summary>
    public static string Encrypt(string source, string key, bool needIV = true)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] encryptedBytes = Encrypt(sourceBytes, key, needIV);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// 解密字符串, 输入为Base64编码的字符串
    /// </summary>
    public static string Decrypt(string source, string key, bool needIV = true)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        byte[] encryptedBytes = Convert.FromBase64String(source);
        byte[] decryptedBytes = Decrypt(encryptedBytes, key, needIV);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// 生成随机密钥
    /// </summary>
    private static string GenerateRandomKey()
    {
        using var rng = RandomNumberGenerator.Create();
        byte[] keyBytes = new byte[KeySize];
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }

    /// <summary>
    /// 从字符串获取密钥字节数组（支持Base64或直接字符串）
    /// </summary>
    private static byte[] GetKeyBytes(string key)
    {
        // 尝试作为Base64解码
        if (key.Length == 44 && key.EndsWith("=")) // Base64编码的32字节
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(key);
                if (decoded.Length == KeySize)
                    return decoded;
            }
            catch
            {
                // 不是有效的Base64，继续使用UTF8
            }
        }

        // 使用UTF8编码，然后使用SHA256哈希确保32字节
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(keyBytes);
    }

    #endregion
}