
namespace Tnzi.IO;

/// <summary>
/// 目录操作辅助类
/// </summary>
public static class DirectoryHelper
{
    /// <summary>
    /// 获取程序根目录
    /// </summary>
    public static string RootPath()
    {
        return Path.GetDirectoryName(typeof(DirectoryHelper).Assembly.Location) ?? string.Empty;
    }

    /// <summary>
    /// 创建文件夹, 如果不存在
    /// </summary>
    /// <param name="directory">要创建的文件夹路径</param>
    public static void CreateIfNotExists(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 递归复制文件夹及文件夹/文件
    /// </summary>
    /// <param name="sourcePath">源文件夹路径</param>
    /// <param name="targetPath">目的文件夹路径</param>
    /// <param name="searchPatterns">要复制的文件扩展名数组（可选）</param>
    public static void Copy(string sourcePath, string targetPath, string[]? searchPatterns = null)
    {
        Check.NotNullOrEmpty(sourcePath);
        Check.NotNullOrEmpty(targetPath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException("The source directory does not exist when copying folders recursively.");
        }

        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        // 复制子目录
        string[] dirs = Directory.GetDirectories(sourcePath);
        foreach (string dir in dirs)
        {
            string dirName = Path.GetFileName(dir);
            Copy(dir, Path.Combine(targetPath, dirName), searchPatterns);
        }

        // 复制文件
        if (searchPatterns != null && searchPatterns.Length > 0)
        {
            foreach (string searchPattern in searchPatterns)
            {
                string[] files = Directory.GetFiles(sourcePath, searchPattern);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(targetPath, fileName), true);
                }
            }
        }
        else
        {
            string[] files = Directory.GetFiles(sourcePath);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(targetPath, fileName), true);
            }
        }
    }

    /// <summary>
    /// 递归删除目录
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <param name="isDeleteRoot">是否删除根目录</param>
    /// <returns>是否成功</returns>
    public static bool Delete(string directory, bool isDeleteRoot = true)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return false;

        try
        {
            // 删除所有子目录和文件
            string[] dirs = Directory.GetDirectories(directory);
            foreach (string dir in dirs)
            {
                Delete(dir, true);
            }

            string[] files = Directory.GetFiles(directory);
            foreach (string file in files)
            {
                File.Delete(file);
            }

            // 删除根目录
            if (isDeleteRoot)
            {
                Directory.Delete(directory);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 清空目录（删除所有子目录和文件，但保留目录本身）
    /// </summary>
    /// <param name="directory">目录路径</param>
    public static void Clear(string directory)
    {
        Delete(directory, false);
    }
}