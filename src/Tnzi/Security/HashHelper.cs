
namespace Tnzi.Security;

/// <summary>
/// 字符串Hash操作类
/// </summary>
public static class HashHelper
{
    /// <summary>
    /// 获取字符串的MD5哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetMd5(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        byte[] bytes = encoding.GetBytes(value);
        return GetMd5(bytes);
    }

    /// <summary>
    /// 获取字节数组的MD5哈希值
    /// </summary>
    public static string GetMd5(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);

        using var hash = MD5.Create();
        bytes = hash.ComputeHash(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取文件流的MD5哈希值
    /// </summary>
    public static string GetMd5(Stream stream)
    {
        Check.NotNull(stream);

        stream.Seek(0, SeekOrigin.Begin);
        using var hash = MD5.Create();
        byte[] hashBytes = hash.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA1哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha1(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA1.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA256哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha256(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA512哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha512(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA512.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字节数组的SHA256哈希值
    /// </summary>
    public static string GetSha256(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);

        using var hash = SHA256.Create();
        byte[] hashBytes = hash.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取文件流的SHA256哈希值
    /// </summary>
    public static string GetSha256(Stream stream)
    {
        Check.NotNull(stream);

        stream.Seek(0, SeekOrigin.Begin);
        using var hash = SHA256.Create();
        byte[] hashBytes = hash.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}