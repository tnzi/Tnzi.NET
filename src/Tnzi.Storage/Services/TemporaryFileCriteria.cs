namespace Tnzi.Storage.Services;

/// <summary>
/// 「哪些临时文件该回收了」的判据。后台清理（<see cref="FileCleanupService"/>）与管理端的列表与手动清理
/// （<see cref="FileReferenceService"/>）用同一份，所以列表里看到的正是清理会删的那些。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>临时文件是记录，不是引用行。</b><c>POST files/upload</c> 与分片上传落下的是 <c>IsTemporary = true</c>、
/// 计数从 0 起的 <see cref="FileRecord"/>，一条引用行都不写。此前两处都只遍历临时引用行，而框架里唯一会写那种行的是
/// <c>SaveWithReferenceAsync(isTemporary: true)</c>：维护页的列表恒为空、手动清理恒报 0，
/// <c>TemporaryFileRetentionHours</c> 对真正的临时上传从不生效。
/// </para>
/// <para>
/// ★ <b>归档进目录的临时记录不算</b>，与孤儿回收同一条豁免：那是「文件库直传落临时记录、挪进目录只改 FolderId」
/// 那段时期留下的行，没有任何引用行、计数为 0，回收它等于删掉用户整理在文件库里的文件。
/// </para>
/// </remarks>
internal static class TemporaryFileCriteria
{
    /// <summary>过了保留期、无人持有、没有归档进目录的临时记录。</summary>
    /// <param name="cutoffUtc">早于这个时刻创建的才算过期。</param>
    public static Expression<Func<FileRecord, bool>> Expired(DateTime cutoffUtc)
        => f => f.IsTemporary
                && f.ReferenceCount <= 0
                && f.FolderId == null
                && f.CreationTime < cutoffUtc;
}
