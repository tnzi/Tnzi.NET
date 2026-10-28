namespace Tnzi.Storage.Dtos;

/// <summary>
/// <see cref="Services.IFileStorageService.SaveAsync(string, Stream, FileSaveOptions, CancellationToken)"/> 的选项。
/// </summary>
public sealed class FileSaveOptions
{
    /// <summary>
    /// 临时文件（引用计数从 0 起，没被任何实体字段引用的话由清理任务回收）。默认 false：正式记录，上传本身就是持有者。
    /// </summary>
    public bool IsTemporary { get; init; }

    /// <summary>标记为公开可读（见 <see cref="Entities.FileRecord.IsPublic"/>）。默认 false。</summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// 正式上传是否复用所有者已有的、字节完全相同的记录（计数 +1 而不是新建一条）。默认 true，与旧重载相同。
    /// </summary>
    /// <remarks>
    /// 文件空间里的每一次上传都是一个独立的条目，要传 <c>false</c>：复用的是整条记录（名字、目录、标签都跟着走），
    /// 同一份 PDF 先存进 <c>/A</c>、再存进 <c>/B</c>，去重会让两次上传拿到同一条记录，第二次归档把它从 <c>/A</c> 挪走。
    /// 临时上传从不去重，这个值对它不起作用。
    /// </remarks>
    public bool Deduplicate { get; init; } = true;

    /// <summary>
    /// 记录归属的用户（写进 <see cref="Entities.FileRecord.CreatorId"/>，配额也记在他名下）。<c>null</c> = 当前用户。
    /// </summary>
    /// <remarks>
    /// ★ 这是一次<b>系统身份</b>的写入：服务端代某个用户存文件（AI 生成的摘要、应用产出的报表）时用，
    /// 后台任务里没有当前用户，不指定所有者的记录不属于任何人、谁的文件空间里都看不到。
    /// <b>绝不能取自请求输入</b> —— 那等于让调用方把文件塞进任意一个用户的名下、记在他的配额上。
    /// </remarks>
    public Guid? OwnerId { get; init; }
}
