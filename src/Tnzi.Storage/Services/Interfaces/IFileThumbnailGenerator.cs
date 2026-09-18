namespace Tnzi.Storage.Services;

/// <summary>
/// 把一个<b>已经交给 provider 的</b>对象画成缩略图，再把缩略图交给 provider。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <see cref="FileStorageService"/> 每条会产出缩略图的写路径（直传 / MD5 直存 / 复制 / 记录在而对象丢了时的重传）
/// 与存量回填共用的那一个协作者：「这种文件此刻画不画得出来」与「怎么画」只在这里回答一次，
/// 而不是四处各抄一遍 <c>IsThumbnailable &amp;&amp; AutoGenerateThumbnail</c>。
/// </para>
/// <para>
/// 默认实现认两类源：**位图**（本机解码器读得了的图片格式，出正方形）与 **PDF**（首页，整页等比缩进盒子；
/// 需要可选包 <c>Tnzi.Documents</c> 的 <c>IPdfRasterizer</c>，其契约在核心，故本模块不引用那个包）。
/// 以 <c>TryAdd</c> 注册：要给视频截帧或换一套出图规则的消费方整体替换它即可。
/// </para>
/// <para>
/// ★ <b>缩略图是尽力而为。</b><see cref="GenerateAsync"/> 画不出来时返回 <c>null</c> 并记日志，<b>绝不抛</b> ——
/// 原件那时已经存好了，一张图画不出来不该让上传失败或回滚。
/// </para>
/// </remarks>
public interface IFileThumbnailGenerator
{
    /// <summary>
    /// 此刻画得出缩略图的全部扩展名（小写、带点）。
    /// </summary>
    /// <remarks>
    /// 给要把 <see cref="CanGenerate"/> 翻译成 SQL 的调用方（回填按 <c>Extension IN (...)</c> 挑候选）。
    /// 「此刻」是认真的：<c>.pdf</c> 只在可选包加载了、原生库装得上、且 <c>Storage:PdfThumbnail:Enabled</c>
    /// 时才在列表里。
    /// </remarks>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// 这种扩展名此刻画不画得出缩略图。不看 <c>AutoGenerateThumbnail</c>：那是「上传时要不要自动画」的开关，
    /// 由写路径自己叠加；回填是管理员显式要求的，不受它约束。
    /// </summary>
    /// <param name="extension">扩展名（带点）；空或 null 答否。</param>
    bool CanGenerate(string? extension);

    /// <summary>
    /// 从已落库的原件派生缩略图并交给 provider。
    /// </summary>
    /// <param name="originalPath">原件在 provider 里的路径（<c>FileRecord.Path</c>）。</param>
    /// <param name="originalKey">原件的存储键（<c>FileRecord.FileName</c>）；缩略图的键从它派生。</param>
    /// <param name="extension">权威扩展名（<c>FileRecord.Extension</c>，带点），决定走哪条出图路径。
    /// 不从 <paramref name="originalKey"/> 里猜：键里的后缀只在扩展名合安全形态时才带。</param>
    /// <param name="size">原件字节数（<c>FileRecord.Size</c>）；0 表示不知道。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缩略图在 provider 里的路径；画不出来（格式不支持、有口令、损坏、超限、原生库缺失）返回 <c>null</c>。</returns>
    Task<string?> GenerateAsync(string originalPath, string originalKey, string? extension, long size, CancellationToken cancellationToken = default);
}
