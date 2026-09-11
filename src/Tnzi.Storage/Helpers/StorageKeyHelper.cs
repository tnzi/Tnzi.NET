namespace Tnzi.Storage.Helpers;

/// <summary>
/// 存储键（交给 <see cref="IFileStorage.UploadAsync"/> 的那个名字）的<b>唯一</b>出处。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>不变量：存储键永远由服务端生成，绝不取自调用方给的名字。</b>
/// 对象存储上键就是对象名、没有日期前缀，而 <c>PutObject</c> 默认覆盖 —— 键若来自调用方，
/// 任何能读到一条记录的人（<c>FileRecordDto.fileName</c> 对能读该记录的人可见）就能用同名再传一次，
/// 把别人那份文件的字节换掉，<c>CanWriteAsync</c> 整条判据链被绕过。本地磁盘上没有攻击也会出事：
/// 两个人同一天上传同名文件，第二份静默截断写覆盖第一份，而第一条记录的 <c>Md5Hash</c> 与
/// <c>Size</c> 仍是旧值，下载给出的却是别人的内容。
/// </para>
/// <para>
/// 2026-09-04 之前这条不变量只写在七处调用点的习惯里，没有文字、没有测试 —— 于是它在
/// 压缩 / 解压 / 分片完成三条写路径上各漂了一次，三处都把调用方给的名字直接当了键。
/// 现在键只从这里出：调用方给的名字一律进 <c>FileRecord.OriginalName</c>（展示用），
/// <c>FileRecord.FileName</c> 与存储键相同。守着它的是 <c>StorageKeyInvariantTests</c>（行为）
/// 与 <c>StorageKeyCallSiteGateTests</c>（每个 <c>UploadAsync</c> 调用点的键都必须从这里出）。
/// </para>
/// <para>
/// 键里的扩展名只保留 <c>.</c> 加字母数字：它只是给磁盘上的文件一个可辨认的后缀，
/// 权威的扩展名在 <c>FileRecord.Extension</c>。不合形态的扩展名直接不带（不会让一次保存失败）。
/// </para>
/// </remarks>
public static class StorageKeyHelper
{
    /// <summary>键里允许带的扩展名形态：点 + 1 到 16 位字母数字。</summary>
    private static readonly Regex SafeExtension = new("^\\.[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 顺序 GUID（<c>D</c> 格式，36 位小写）+ 可选的安全扩展名。
    /// </summary>
    private static readonly Regex GeneratedShape = new(
        "^(?:thumb_)?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:\\.[A-Za-z0-9]{1,16})?$",
        RegexOptions.CultureInvariant);

    /// <summary>分片临时对象：<c>chunk_{会话 id}_{序号}</c>，两段都是服务端产生的值。</summary>
    private static readonly Regex ChunkShape = new(
        "^chunk_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}_\\d+$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 为一个新对象生成存储键：顺序 GUID + 安全形态的扩展名。
    /// </summary>
    /// <param name="extension">
    /// 扩展名（通常来自 <c>Path.GetExtension(originalName)</c>，带点）。
    /// 为空或不合安全形态时键不带扩展名。
    /// </param>
    public static string NewKey(string? extension)
    {
        var suffix = !string.IsNullOrEmpty(extension) && SafeExtension.IsMatch(extension)
            ? extension.ToLowerInvariant()
            : string.Empty;

        return $"{SequentialGuid.NewGuid()}{suffix}";
    }

    /// <summary>缩略图的键：从原件的（已生成的）键派生。</summary>
    public static string ThumbnailKey(string originalKey)
    {
        Check.NotNullOrWhiteSpace(originalKey);
        return $"thumb_{originalKey}";
    }

    /// <summary>分片临时对象的键：会话 id 与序号都是服务端产生的值。</summary>
    public static string ChunkKey(Guid uploadSessionId, int chunkIndex)
        => $"chunk_{uploadSessionId:D}_{chunkIndex}";

    /// <summary>
    /// 这个键是不是本类能生成出来的形态。供测试断言「没有一条写路径把调用方的名字当了键」。
    /// </summary>
    public static bool IsGenerated(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        return GeneratedShape.IsMatch(key) || ChunkShape.IsMatch(key);
    }
}
