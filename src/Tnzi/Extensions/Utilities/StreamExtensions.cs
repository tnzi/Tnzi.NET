
namespace Tnzi.Extensions;

/// <summary>
/// Stream扩展方法
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// 读取 Stream 内容并转换为字符串
    /// </summary>
    public static string ReadAsString(this Stream stream, Encoding? encoding = null)
    {
        Check.NotNull(stream);

        encoding ??= Encoding.UTF8;

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using (StreamReader reader = new StreamReader(stream, encoding, true, 1024, true))
        {
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// 把Stream转换为字节数组
    /// </summary>
    public static byte[] ToByteArray(this Stream stream)
    {
        Check.NotNull(stream);

        if (stream is MemoryStream memoryStream)
            return memoryStream.ToArray();

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// 将字节数组转换为Stream
    /// </summary>
    public static Stream ToStream(this byte[] bytes)
    {
        Check.NotNull(bytes);

        return new MemoryStream(bytes);
    }
}