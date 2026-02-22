
namespace Tnzi.Utilities;

/// <summary>
/// 压缩操作类
/// </summary>
public static class Compression
{
    /// <summary>
    /// 对byte数组进行压缩
    /// </summary>
    /// <param name="data">待压缩的byte数组</param>
    /// <returns>压缩后的byte数组</returns>
    public static byte[] Compress(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        using (MemoryStream ms = new MemoryStream())
        {
            using (GZipStream zip = new GZipStream(ms, CompressionMode.Compress, true))
            {
                zip.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }
    }

    /// <summary>
    /// 对byte[]数组进行解压
    /// </summary>
    /// <param name="data">待解压的byte数组</param>
    /// <returns>解压后的byte数组</returns>
    public static byte[] Decompress(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        using (MemoryStream tmpMs = new MemoryStream())
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (GZipStream zip = new GZipStream(ms, CompressionMode.Decompress, true))
                {
                    zip.CopyTo(tmpMs);
                }
            }
            return tmpMs.ToArray();
        }
    }

    /// <summary>
    /// 对字符串进行压缩
    /// </summary>
    /// <param name="value">待压缩的字符串</param>
    /// <returns>压缩后的字符串</returns>
    public static string Compress(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        bytes = Compress(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 对字符串进行解压
    /// </summary>
    /// <param name="value">待解压的字符串</param>
    /// <returns>解压后的字符串</returns>
    public static string Decompress(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        byte[] bytes = Convert.FromBase64String(value);
        bytes = Decompress(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 将文件夹压缩成zip文件
    /// </summary>
    public static void Zip(string sourceDir, string zipFile)
    {
        Check.NotNullOrEmpty(sourceDir);
        Check.NotNullOrEmpty(zipFile);

        ZipFile.CreateFromDirectory(sourceDir, zipFile);
    }

    /// <summary>
    /// 将zip文件解压到指定文件夹
    /// </summary>
    public static void UnZip(string zipFile, string targetDir)
    {
        Check.NotNullOrEmpty(zipFile);
        Check.NotNullOrEmpty(targetDir);

        ZipFile.ExtractToDirectory(zipFile, targetDir, overwriteFiles: true);
    }
}