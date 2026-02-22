
namespace Tnzi.IO;

/// <summary>
/// 文件辅助操作类
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// 创建文件, 如果文件不存在
    /// </summary>
    /// <param name="fileName">要创建的文件</param>
    public static void CreateIfNotExists(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        if (File.Exists(fileName))
            return;

        string? dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir))
        {
            DirectoryHelper.CreateIfNotExists(dir);
        }

        File.Create(fileName).Dispose();
    }

    /// <summary>
    /// 如果目录不存在则创建目录
    /// </summary>
    /// <param name="fileName">文件路径</param>
    public static void CreateDirectoryIfNotExists(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        string? dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir))
        {
            DirectoryHelper.CreateIfNotExists(dir);
        }
    }

    /// <summary>
    /// 删除指定文件（如果存在）
    /// </summary>
    /// <param name="fileName">要删除的文件名</param>
    public static void DeleteIfExists(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return;

        File.Delete(fileName);
    }

    /// <summary>
    /// 设置或取消文件的指定<see cref="FileAttributes"/>属性
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="attribute">要设置的文件属性</param>
    /// <param name="isSet">true为设置, false为取消</param>
    public static void SetAttribute(string fileName, FileAttributes attribute, bool isSet)
    {
        Check.NotNullOrEmpty(fileName);

        var fi = new FileInfo(fileName);
        if (!fi.Exists)
        {
            throw new FileNotFoundException("File does not exist.", fileName);
        }

        if (isSet)
        {
            fi.Attributes |= attribute;
        }
        else
        {
            fi.Attributes &= ~attribute;
        }
    }

    /// <summary>
    /// 获取文件版本号
    /// </summary>
    /// <param name="fileName">完整文件名</param>
    /// <returns>文件版本号</returns>
    public static string? GetVersion(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return null;

        var fvi = FileVersionInfo.GetVersionInfo(fileName);
        return fvi.FileVersion;
    }

    /// <summary>
    /// 获取文件大小（字节）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>文件大小</returns>
    public static long GetFileSize(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return 0;

        var fi = new FileInfo(fileName);
        return fi.Length;
    }

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>扩展名（包含点号）</returns>
    public static string GetExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        return Path.GetExtension(fileName);
    }

    /// <summary>
    /// 获取文件名（不含扩展名）
    /// </summary>
    /// <param name="fileName">完整文件名</param>
    /// <returns>文件名（不含扩展名）</returns>
    public static string GetFileNameWithoutExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>
    /// 读取文件所有文本
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <returns>文件内容</returns>
    public static string ReadAllText(string fileName, System.Text.Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return string.Empty;

        encoding ??= System.Text.Encoding.UTF8;
        return File.ReadAllText(fileName, encoding);
    }

    /// <summary>
    /// 写入文件所有文本
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="content">内容</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    public static void WriteAllText(string fileName, string content, System.Text.Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(fileName);

        CreateDirectoryIfNotExists(fileName);
        encoding ??= System.Text.Encoding.UTF8;
        File.WriteAllText(fileName, content, encoding);
    }

    /// <summary>
    /// 根据路径查找文件并返回文件流
    /// </summary>
    /// <param name="filePath">文件路径（绝对路径或相对路径）</param>
    /// <returns>文件流，如果文件不存在则返回 null</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static Stream? OpenRead(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            return null;

        return File.OpenRead(filePath);
    }

    /// <summary>
    /// 根据路径查找文件并返回文件流（如果文件不存在则抛出异常）
    /// </summary>
    /// <param name="filePath">文件路径（绝对路径或相对路径）</param>
    /// <returns>文件流</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static Stream OpenReadOrThrow(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        return File.OpenRead(filePath);
    }

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>如果文件存在则返回 true，否则返回 false</returns>
    public static bool Exists(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        return File.Exists(filePath);
    }

    /// <summary>
    /// 读取文件的所有字节
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件的字节数组，如果文件不存在则返回空数组</returns>
    public static byte[] ReadAllBytes(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return Array.Empty<byte>();

        return File.ReadAllBytes(filePath);
    }

    /// <summary>
    /// 读取文件的所有字节（如果文件不存在则抛出异常）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件的字节数组</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static byte[] ReadAllBytesOrThrow(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        return File.ReadAllBytes(filePath);
    }

    /// <summary>
    /// 将字节数组写入文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="bytes">要写入的字节数组</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static void WriteAllBytes(string filePath, byte[] bytes)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        File.WriteAllBytes(filePath, bytes ?? Array.Empty<byte>());
    }

    /// <summary>
    /// 读取文件的所有行
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <returns>文件的所有行，如果文件不存在则返回空数组</returns>
    public static string[] ReadAllLines(string filePath, Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return Array.Empty<string>();

        encoding ??= Encoding.UTF8;
        return File.ReadAllLines(filePath, encoding);
    }

    /// <summary>
    /// 读取文件的所有行（如果文件不存在则抛出异常）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <returns>文件的所有行</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static string[] ReadAllLinesOrThrow(string filePath, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        encoding ??= Encoding.UTF8;
        return File.ReadAllLines(filePath, encoding);
    }

    /// <summary>
    /// 将字符串数组写入文件（每行一个字符串）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="lines">要写入的行数组</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static void WriteAllLines(string filePath, IEnumerable<string> lines, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        encoding ??= Encoding.UTF8;
        File.WriteAllLines(filePath, lines ?? Enumerable.Empty<string>(), encoding);
    }

    /// <summary>
    /// 将文本追加到文件末尾
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">要追加的内容</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static void AppendAllText(string filePath, string content, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        encoding ??= Encoding.UTF8;
        File.AppendAllText(filePath, content ?? string.Empty, encoding);
    }

    /// <summary>
    /// 将字符串数组追加到文件末尾（每行一个字符串）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="lines">要追加的行数组</param>
    /// <param name="encoding">编码（默认UTF8）</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static void AppendAllLines(string filePath, IEnumerable<string> lines, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        encoding ??= Encoding.UTF8;
        File.AppendAllLines(filePath, lines ?? Enumerable.Empty<string>(), encoding);
    }

    /// <summary>
    /// 复制文件
    /// </summary>
    /// <param name="sourceFileName">源文件路径</param>
    /// <param name="destFileName">目标文件路径</param>
    /// <param name="overwrite">如果目标文件已存在，是否覆盖（默认false）</param>
    /// <exception cref="ArgumentNullException">当 sourceFileName 或 destFileName 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当源文件不存在时抛出</exception>
    public static void Copy(string sourceFileName, string destFileName, bool overwrite = false)
    {
        Check.NotNullOrEmpty(sourceFileName);
        Check.NotNullOrEmpty(destFileName);

        if (!File.Exists(sourceFileName))
            throw new FileNotFoundException($"Source file not found: {sourceFileName}", sourceFileName);

        CreateDirectoryIfNotExists(destFileName);
        File.Copy(sourceFileName, destFileName, overwrite);
    }

    /// <summary>
    /// 移动文件
    /// </summary>
    /// <param name="sourceFileName">源文件路径</param>
    /// <param name="destFileName">目标文件路径</param>
    /// <exception cref="ArgumentNullException">当 sourceFileName 或 destFileName 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当源文件不存在时抛出</exception>
    public static void Move(string sourceFileName, string destFileName)
    {
        Check.NotNullOrEmpty(sourceFileName);
        Check.NotNullOrEmpty(destFileName);

        if (!File.Exists(sourceFileName))
            throw new FileNotFoundException($"Source file not found: {sourceFileName}", sourceFileName);

        CreateDirectoryIfNotExists(destFileName);
        File.Move(sourceFileName, destFileName);
    }

    /// <summary>
    /// 获取文件名（包含扩展名）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件名，如果路径为空则返回空字符串</returns>
    public static string GetFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        return Path.GetFileName(filePath);
    }

    /// <summary>
    /// 获取目录名
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>目录名，如果路径为空则返回空字符串</returns>
    public static string? GetDirectoryName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        return Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// 获取完整路径
    /// </summary>
    /// <param name="path">路径</param>
    /// <returns>完整路径</returns>
    /// <exception cref="ArgumentNullException">当 path 为空时抛出</exception>
    public static string GetFullPath(string path)
    {
        Check.NotNullOrEmpty(path);

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 打开文件用于写入（如果文件不存在则创建）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件流</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static Stream OpenWrite(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        return File.OpenWrite(filePath);
    }

    /// <summary>
    /// 打开文件用于追加（如果文件不存在则创建）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件流</returns>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    public static Stream OpenAppend(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        CreateDirectoryIfNotExists(filePath);
        return File.Open(filePath, FileMode.Append, FileAccess.Write);
    }

    /// <summary>
    /// 获取文件的最后写入时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>最后写入时间，如果文件不存在则返回 null</returns>
    public static DateTime? GetLastWriteTime(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        return File.GetLastWriteTime(filePath);
    }

    /// <summary>
    /// 获取文件的最后访问时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>最后访问时间，如果文件不存在则返回 null</returns>
    public static DateTime? GetLastAccessTime(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        return File.GetLastAccessTime(filePath);
    }

    /// <summary>
    /// 获取文件的创建时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>创建时间，如果文件不存在则返回 null</returns>
    public static DateTime? GetCreationTime(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        return File.GetCreationTime(filePath);
    }

    /// <summary>
    /// 设置文件的最后写入时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="dateTime">要设置的时间</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static void SetLastWriteTime(string filePath, DateTime dateTime)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        File.SetLastWriteTime(filePath, dateTime);
    }

    /// <summary>
    /// 设置文件的最后访问时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="dateTime">要设置的时间</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static void SetLastAccessTime(string filePath, DateTime dateTime)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        File.SetLastAccessTime(filePath, dateTime);
    }

    /// <summary>
    /// 设置文件的创建时间
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="dateTime">要设置的时间</param>
    /// <exception cref="ArgumentNullException">当 filePath 为空时抛出</exception>
    /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
    public static void SetCreationTime(string filePath, DateTime dateTime)
    {
        Check.NotNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        File.SetCreationTime(filePath, dateTime);
    }
}