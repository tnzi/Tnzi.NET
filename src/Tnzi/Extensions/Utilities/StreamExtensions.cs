
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
    /// 将Stream保存到文件
    /// </summary>
    public static bool ToFile(this Stream srcStream, string path)
    {
        if (srcStream == null)
            return false;

        const int BuffSize = 32768;
        bool result = true;
        Stream? dstStream = null;
        byte[] buffer = new byte[BuffSize];

        try
        {
            using (dstStream = File.OpenWrite(path))
            {
                int len;
                while ((len = srcStream.Read(buffer, 0, BuffSize)) > 0)
                    dstStream.Write(buffer, 0, len);
            }
        }
        catch
        {
            result = false;
        }

        return result && File.Exists(path);
    }

    /// <summary>
    /// 将字节数组转换为Stream
    /// </summary>
    public static Stream ToStream(this byte[] bytes)
    {
        Check.NotNull(bytes);

        return new MemoryStream(bytes);
    }

    /// <summary>
    /// 比较两个Stream的内容是否相等
    /// </summary>
    public static bool ContentsEqual(this Stream src, Stream other)
    {
        Check.NotNull(src);
        Check.NotNull(other);

        if (src.Length != other.Length)
            return false;

        const int bufferSize = 2048;
        byte[] buffer1 = new byte[bufferSize];
        byte[] buffer2 = new byte[bufferSize];

        src.Position = 0;
        other.Position = 0;

        while (true)
        {
            int len1 = src.Read(buffer1, 0, bufferSize);
            int len2 = other.Read(buffer2, 0, bufferSize);

            if (len1 != len2)
                return false;

            if (len1 == 0)
                return true;

            int iterations = (int)Math.Ceiling((double)len1 / sizeof(long));
            for (int i = 0; i < iterations; i++)
            {
                if (BitConverter.ToInt64(buffer1, i * sizeof(long)) != BitConverter.ToInt64(buffer2, i * sizeof(long)))
                {
                    return false;
                }
            }
        }
    }
}