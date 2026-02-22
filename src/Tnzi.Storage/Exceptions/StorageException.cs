namespace Tnzi.Storage.Exceptions;

/// <summary>
/// 存储异常基类
/// </summary>
public class StorageException : BusinessException
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// 初始化 <see cref="StorageException"/> 类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码</param>
    public StorageException(
        string message,
        string? filePath = null,
        string errorCode = ErrorCodes.FILE_STORAGE_ERROR,
        int httpStatusCode = 400)
        : base(message, errorCode, httpStatusCode)
    {
        FilePath = filePath;
        if (filePath != null)
        {
            this.WithData("FilePath", filePath);
        }
    }
}

/// <summary>
/// 文件上传异常
/// </summary>
public class FileUploadException : StorageException
{
    /// <summary>
    /// 文件大小
    /// </summary>
    public long? FileSize { get; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// 初始化 <see cref="FileUploadException"/> 类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="fileName">文件名</param>
    /// <param name="fileSize">文件大小</param>
    /// <remarks>基类 StorageException.FilePath 在此处被赋值为 fileName（上传场景下无完整路径时用文件名标识）</remarks>
    public FileUploadException(string message, string? fileName = null, long? fileSize = null)
        : base(message, fileName, ErrorCodes.FILE_UPLOAD_ERROR)
    {
        FileName = fileName;
        FileSize = fileSize;
        if (fileSize.HasValue)
        {
            this.WithData("FileSize", fileSize.Value);
        }
    }
}

/// <summary>
/// 文件未找到异常
/// </summary>
public class FileNotFoundException : StorageException
{
    /// <summary>
    /// 初始化 <see cref="FileNotFoundException"/> 类型的新实例
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public FileNotFoundException(string filePath)
        : base($"File '{filePath}' was not found.", filePath, ErrorCodes.FILE_NOT_FOUND, 404)
    {
    }

    /// <summary>
    /// 初始化 <see cref="FileNotFoundException"/> 类型的新实例（自定义消息）
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="filePath">文件路径</param>
    public FileNotFoundException(string message, string filePath)
        : base(message, filePath, ErrorCodes.FILE_NOT_FOUND, 404)
    {
    }
}

/// <summary>
/// 文件下载异常
/// </summary>
public class FileDownloadException : StorageException
{
    /// <summary>
    /// 初始化 <see cref="FileDownloadException"/> 类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="filePath">文件路径</param>
    public FileDownloadException(string message, string? filePath = null)
        : base(message, filePath, ErrorCodes.FILE_DOWNLOAD_ERROR)
    {
    }
}

/// <summary>
/// 文件删除异常
/// </summary>
public class FileDeleteException : StorageException
{
    /// <summary>
    /// 初始化 <see cref="FileDeleteException"/> 类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="filePath">文件路径</param>
    public FileDeleteException(string message, string? filePath = null)
        : base(message, filePath, ErrorCodes.FILE_DELETE_ERROR)
    {
    }
}

/// <summary>
/// 文件大小超限异常
/// </summary>
public class FileSizeExceededException : FileUploadException
{
    /// <summary>
    /// 最大允许大小（字节）
    /// </summary>
    public long MaxSize { get; }

    /// <summary>
    /// 初始化 <see cref="FileSizeExceededException"/> 类型的新实例
    /// </summary>
    /// <param name="fileSize">文件大小</param>
    /// <param name="maxSize">最大允许大小</param>
    /// <param name="fileName">文件名</param>
    public FileSizeExceededException(long fileSize, long maxSize, string? fileName = null)
        : base($"File size ({fileSize} bytes) exceeds maximum allowed size ({maxSize} bytes).", fileName, fileSize)
    {
        MaxSize = maxSize;
        this.WithData("MaxSize", maxSize);
    }
}

/// <summary>
/// 文件类型不支持异常
/// </summary>
public class UnsupportedFileTypeException : FileUploadException
{
    /// <summary>
    /// 文件扩展名
    /// </summary>
    public string? FileExtension { get; }

    /// <summary>
    /// 初始化 <see cref="UnsupportedFileTypeException"/> 类型的新实例
    /// </summary>
    /// <param name="fileExtension">文件扩展名</param>
    /// <param name="fileName">文件名</param>
    public UnsupportedFileTypeException(string? fileExtension, string? fileName = null)
        : base($"File type '{fileExtension}' is not supported.", fileName)
    {
        FileExtension = fileExtension;
        if (fileExtension != null)
        {
            this.WithData("FileExtension", fileExtension);
        }
    }
}
