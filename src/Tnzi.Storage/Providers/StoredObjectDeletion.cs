namespace Tnzi.Storage.Providers;

/// <summary>
/// 删除一个存储对象并确认它已不在。
/// </summary>
/// <remarks>
/// <see cref="IFileStorage.DeleteAsync"/> 的 <c>false</c> 有两种意思：删除真的失败了（内置的 S3 / R2 / Azure / Local
/// 都把异常吞掉改报 <c>false</c>），或者对象本来就不在（Local 的文件不存在、Azure 的 <c>DeleteIfExists</c>）。
/// 前者必须以失败离开，后者是成功：删除是幂等的，重做一次删除不该被当成故障。只有再问一次
/// <see cref="IFileStorage.ExistsAsync"/> 才分得清这两种。
/// <para>
/// 只看返回值而不复核，等于把每一次瞬时故障都当作删除成功：调用方随后删掉指向它的记录，
/// 对象从此没有任何记录可以找回，任何按记录枚举的清理都看不见它。
/// </para>
/// </remarks>
internal static class StoredObjectDeletion
{
    /// <summary>
    /// 删除 <paramref name="path"/> 指向的对象。
    /// </summary>
    /// <returns>对象已不在存储里（删掉了或本来就不在）时为 <c>true</c>；删除没成功、对象仍在时为 <c>false</c>。
    /// provider 抛出的异常照常抛出。</returns>
    public static async Task<bool> DeleteAndConfirmAsync(IFileStorage storage, string path)
    {
        if (await storage.DeleteAsync(path))
            return true;

        return !await storage.ExistsAsync(path);
    }
}
