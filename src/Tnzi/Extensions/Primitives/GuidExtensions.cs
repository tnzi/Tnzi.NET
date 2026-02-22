
namespace Tnzi.Extensions;

/// <summary>
/// Guid扩展方法
/// </summary>
public static class GuidExtensions
{
    /// <summary>
    /// 判断Guid是否为空
    /// </summary>
    public static bool IsEmpty(this Guid? guid)
    {
        return guid == null || guid == Guid.Empty;
    }

    /// <summary>
    /// 判断Guid是否为空
    /// </summary>
    public static bool IsEmpty(this Guid guid)
    {
        return guid == Guid.Empty;
    }

    /// <summary>
    /// 生成一个8位十六进制Token
    /// 通过 XOR 操作将 GUID 的16字节压缩为4字节，然后转换为8位十六进制字符串
    /// 注意：此方法仅用于生成简单的标识符，不适用于安全场景
    /// </summary>
    /// <param name="guid">源 GUID，如果为空则使用新生成的 GUID</param>
    /// <returns>8位十六进制字符串（如 "A1B2C3D4"）</returns>
    public static string ToToken(this Guid guid)
    {
        var buffer = guid.ToByteArray();
        // 将16字节的GUID通过XOR操作压缩为4字节
        var tokenValue = BitConverter.ToUInt32(buffer, 0)
                        ^ BitConverter.ToUInt32(buffer, 4)
                        ^ BitConverter.ToUInt32(buffer, 8)
                        ^ BitConverter.ToUInt32(buffer, 12);
        return tokenValue.ToString("X8");
    }

    /// <summary>
    /// 生成一个8位十六进制Token（使用新生成的 GUID）
    /// 通过 XOR 操作将 GUID 的16字节压缩为4字节，然后转换为8位十六进制字符串
    /// 注意：此方法仅用于生成简单的标识符，不适用于安全场景
    /// </summary>
    /// <returns>8位十六进制字符串（如 "A1B2C3D4"）</returns>
    public static string NewToken()
    {
        return Guid.NewGuid().ToToken();
    }
}