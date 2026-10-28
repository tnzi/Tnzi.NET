namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// 回收站里的一项：用户删掉的一个文件，或一个目录（连同它下面的一切）。
/// </summary>
/// <remarks>
/// <para>
/// <b>文件</b>进回收站时记录本身一个字都不改（目录、字节、引用计数都留着），只靠这一行把它从文件空间的列表与搜索里摘掉；
/// 字节因此在清除之前一直计入所有者的配额。<b>目录</b>进回收站时它与它下面的每个目录都软删、并记下本条目的 id
/// （<see cref="FileFolder.RecycleBinEntryId"/>），里面的文件跟着目录一起消失。
/// </para>
/// <para>
/// 永久删除（手动、清空回收站、过期自动清除）= 删掉这一行，并以系统身份放掉文件空间对每个文件的持有：
/// 没有别的记录引用的文件连字节一起删掉；还被聊天消息之类引用的留下，只是不再出现在文件空间里。
/// </para>
/// </remarks>
public class RecycleBinEntry : EntityBase<Guid>, IHasCreationTime, IHasCreator, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>这是谁的回收站（被删的文件或目录的所有者，不一定是动手删的人）。</summary>
    public Guid OwnerId { get; set; }

    /// <summary>被删的是文件还是目录。</summary>
    public RecycleBinItemType ItemType { get; set; }

    /// <summary>文件记录或目录的 id。</summary>
    public Guid ItemId { get; set; }

    /// <summary>删除那一刻的名字（文件的原始文件名 / 目录名），供回收站列表展示。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>删除时所在的目录；<c>null</c> = 根目录。还原时回到这里，它不在了就回到根目录。</summary>
    public Guid? OriginalParentId { get; set; }

    /// <summary>删除时所在目录的路径（根目录是 <c>/</c>），只用于展示「从哪里删的」。</summary>
    public string OriginalLocation { get; set; } = "/";

    /// <summary>占用的字节数：文件的大小，或目录里全部文件的大小之和（删除那一刻）。</summary>
    public long Size { get; set; }

    /// <summary>包含的文件数：文件为 1，目录为它下面的文件数（删除那一刻）。</summary>
    public int FileCount { get; set; }

    /// <summary>放进回收站的时间。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>动手删除的人（所有者本人，或替他删除的管理员）。</summary>
    public Guid? CreatorId { get; set; }
}

/// <summary>
/// 回收站里的一项是什么。
/// </summary>
public enum RecycleBinItemType
{
    /// <summary>一个文件。</summary>
    File = 1,

    /// <summary>一个目录，连同它下面的目录与文件。</summary>
    Folder = 2
}
