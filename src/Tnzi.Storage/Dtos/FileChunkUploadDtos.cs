namespace Tnzi.Storage.Dtos;

// 分片上传的 API 契约。与分享 DTO 同一条理由（见 FileShareDtos.cs）：
// 端点留在父模块的 `DefaultStorageController` 上，实现与两张表在 `Tnzi.Storage.Workspace`。
//
// ★ `FileUploadSessionDto` / `FileChunkDto` 是这次拆分**新加**的：此前这两个端点直接把
//   `FileUploadSession` / `FileChunk` **实体**序列化出去。实体搬进子模块后父模块的端点签名
//   引用不到它们，于是补上投影 —— 顺带堵掉了原先随实体一起发出去的 `tenantId`。
//   其余字段逐字保留，与前端 `@tnzi/core` 早就声明的 `FileUploadSessionDto` / `FileChunkDto`
//   完全对齐（那两个 interface 本来就没写 `tenantId`）。

/// <summary>
/// 文件上传进度
/// </summary>
public class FileUploadProgress
{
    /// <summary>
    /// 上传会话ID
    /// </summary>
    public Guid UploadSessionId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 已上传大小
    /// </summary>
    public long UploadedSize { get; set; }

    /// <summary>
    /// 总分片数
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 已上传分片数
    /// </summary>
    public int UploadedChunks { get; set; }

    /// <summary>
    /// 上传进度百分比 0-100
    /// </summary>
    public double ProgressPercentage => TotalSize > 0 ? (double)UploadedSize / TotalSize * 100 : 0;

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 是否已取消
    /// </summary>
    public bool IsCancelled { get; set; }
}
/// <summary>
/// 初始化分块上传请求
/// </summary>
public class InitiateChunkedUploadRequest
{
    /// <summary>
    /// 文件名
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 分块大小（字节），默认 5MB
    /// </summary>
    public int ChunkSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// 整体 MD5（可选，用于校验）
    /// </summary>
    [MaxLength(64)]
    public string? Md5Hash { get; set; }
}
/// <summary>
/// 完成分块上传请求
/// </summary>
public class CompleteChunkedUploadRequest
{
    /// <summary>
    /// 是否临时文件
    /// </summary>
    public bool IsTemporary { get; set; } = false;
}

/// <summary>
/// 分片上传会话对外响应 DTO（<c>POST files/upload/chunk/init</c> 的返回体）。
/// </summary>
/// <remarks>
/// 除 <c>TenantId</c> 外与 <c>FileUploadSession</c> 实体逐字段一致：租户标识是服务端的内部事实，
/// 没有理由随一次上传会话发给浏览器。
/// </remarks>
public class FileUploadSessionDto
{
    /// <summary>会话 ID，后续上传 / 完成 / 取消 / 查进度都用它。</summary>
    public Guid Id { get; set; }

    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件总大小（字节）</summary>
    public long TotalSize { get; set; }

    /// <summary>分块大小（字节）</summary>
    public int ChunkSize { get; set; }

    /// <summary>总分块数</summary>
    public int TotalChunks { get; set; }

    /// <summary>已上传分块数</summary>
    public int UploadedChunks { get; set; }

    /// <summary>已上传大小（字节）</summary>
    public long UploadedSize { get; set; }

    /// <summary>整体 MD5（客户端声明，可选）</summary>
    public string? Md5Hash { get; set; }

    /// <summary>是否已完成</summary>
    public bool IsCompleted { get; set; }

    /// <summary>是否已取消</summary>
    public bool IsCancelled { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>创建者 ID</summary>
    public Guid? CreatorId { get; set; }

    /// <summary>过期时间；过期会话由清理任务连同残留分片一起回收。</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 单个分片对外响应 DTO（<c>POST files/upload/chunk/{sessionId}</c> 的返回体）。
/// </summary>
/// <remarks>
/// 同样只去掉 <c>TenantId</c>。<c>ChunkPath</c> <b>保留</b>：它本就是这个端点一直返回的字段，
/// 去掉会是一次没人要求的契约收窄。
/// </remarks>
public class FileChunkDto
{
    /// <summary>分片记录 ID</summary>
    public Guid Id { get; set; }

    /// <summary>所属上传会话 ID</summary>
    public Guid UploadSessionId { get; set; }

    /// <summary>分块索引（从 0 开始）</summary>
    public int ChunkIndex { get; set; }

    /// <summary>分块大小（字节）</summary>
    public long ChunkSize { get; set; }

    /// <summary>分块临时存储路径</summary>
    public string? ChunkPath { get; set; }

    /// <summary>分块 MD5</summary>
    public string? Md5Hash { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}
