namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 「这个文件在不在某个用户的文件空间里」的唯一判据，列表、搜索与回收站共用（各写一份就会漂开）。
/// </summary>
/// <remarks>
/// <para>
/// 一个文件属于 <c>ownerId</c> 的文件空间，当且仅当它是 <c>ownerId</c> 的（<see cref="FileRecord.CreatorId"/>）、
/// 不在回收站里，并且满足下面两条之一：
/// </para>
/// <list type="bullet">
/// <item><b>放在他的一个目录里</b>：<see cref="FileRecord.FolderId"/> 指向一个他自己的、没被删的目录。</item>
/// <item><b>在根目录</b>：<c>FolderId</c> 为空、是正式记录、且有<b>隐式持有者</b>（计数多于正式引用行数）。</item>
/// </list>
/// <para>
/// ★ 根目录那一条刻意不是「他所有没放进目录的文件」。一个用户名下有很多文件不是他放进文件空间的：头像、
/// 聊天附件是先临时上传、再被业务记录引用的临时记录（<c>IsTemporary</c>，计数等于引用行数）；正式记录里也有只剩
/// 业务记录在持有的（上传者删掉了自己那一份、消息还引用着它），计数同样等于引用行数，<b>没有</b>隐式持有者。
/// 放进文件空间的文件都有：空间里的正式上传本身就是持有者，把文件归档进目录（哪怕挪回根目录）会给它添一份
/// （临时记录转正，正式记录见 <c>FileReferenceCounts.TryTakeImplicitHoldAsync</c>）。于是「文件空间持有它」就是「它在文件空间里」，
/// 永久删除放掉这份持有，文件随之离开文件空间，而仍引用它的那条消息照常可用。
/// </para>
/// <para>
/// ★ 目录那一条要求目录的所有者就是文件的所有者：放在别人目录里的文件（拆分前管理员能跨用户归档）在两个人的空间里
/// 都不出现，而不是让一个人看到另一个人的目录名。
/// </para>
/// </remarks>
internal static class FileSpaceQueries
{
    /// <summary>不在回收站里的文件：本身没有回收站条目。所在目录是否进了回收站由「目录必须没被删」那一条管。</summary>
    public static IQueryable<FileRecord> NotInRecycleBin(IQueryable<FileRecord> files, IQueryable<RecycleBinEntry> entries)
        => files.Where(f => !entries.Any(e => e.ItemType == RecycleBinItemType.File && e.ItemId == f.Id));

    /// <summary><paramref name="ownerId"/> 文件空间根目录里的文件。</summary>
    public static IQueryable<FileRecord> AtRoot(
        IQueryable<FileRecord> files, Guid ownerId, IQueryable<FileReference> references, IQueryable<RecycleBinEntry> entries)
        => NotInRecycleBin(files, entries).Where(f =>
            f.CreatorId == ownerId
            && f.FolderId == null
            && !f.IsTemporary
            && f.ReferenceCount > references.Count(r => r.FileId == f.Id && !r.IsTemporary));

    /// <summary>直接放在 <paramref name="folderId"/>（<paramref name="ownerId"/> 自己的、活着的目录）里的文件。</summary>
    public static IQueryable<FileRecord> InFolder(
        IQueryable<FileRecord> files, Guid ownerId, Guid folderId, IQueryable<RecycleBinEntry> entries)
        => NotInRecycleBin(files, entries).Where(f => f.CreatorId == ownerId && f.FolderId == folderId);

    /// <summary><paramref name="ownerId"/> 文件空间里的全部文件（任意目录 + 根目录），搜索用。</summary>
    /// <param name="files">文件记录。</param>
    /// <param name="ownerId">文件空间的主人。</param>
    /// <param name="liveFolders">目录查询（带软删全局过滤器，所以只有没被删的目录）。</param>
    /// <param name="references">引用行。</param>
    /// <param name="entries">回收站条目。</param>
    public static IQueryable<FileRecord> InSpace(
        IQueryable<FileRecord> files,
        Guid ownerId,
        IQueryable<FileFolder> liveFolders,
        IQueryable<FileReference> references,
        IQueryable<RecycleBinEntry> entries)
        => NotInRecycleBin(files, entries).Where(f =>
            f.CreatorId == ownerId
            && ((f.FolderId != null && liveFolders.Any(folder => folder.Id == f.FolderId && folder.CreatorId == ownerId))
                || (f.FolderId == null
                    && !f.IsTemporary
                    && f.ReferenceCount > references.Count(r => r.FileId == f.Id && !r.IsTemporary))));
}
