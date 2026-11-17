namespace Tnzi.Identity.Options;

/// <summary>
/// 头像文件的约束。配置路径 <c>Identity:Avatar</c>。
/// </summary>
/// <remarks>
/// ★ 约束落在「把文件 id 写进 <c>UserDetail.AvatarId</c>」那一刻，不落在上传端点：
/// 上传走的是存储模块的通用 <c>POST files/upload</c>，它不知道这份文件将来要做头像；
/// 而 <c>AvatarId</c> 是 <c>[FileField(Public = true)]</c>，写进去就是把文件发布给全世界。
/// 所以「只有一张够小的图片才能被发布成头像」只能在写入这一处判定，任何客户端都绕不过去。
/// </remarks>
public class AvatarOptions
{
    /// <summary>默认上限：10 MB。</summary>
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

    /// <summary>
    /// 头像文件的字节上限，默认 10 MB。超过即拒绝写入（400 <c>IDENTITY_AVATAR_INVALID</c>）。
    /// </summary>
    /// <remarks>
    /// 前端在上传前用同一个数做客户端校验；两边不一致时以服务端为准，前端那一道只是让用户少等一次往返。
    /// </remarks>
    public long MaxBytes { get; set; } = DefaultMaxBytes;
}
