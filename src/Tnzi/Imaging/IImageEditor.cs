namespace Tnzi.Imaging;

/// <summary>
/// 在服务端对图像做遮挡、模糊、裁切与缩放。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在核心、实现在 <c>Tnzi.Imaging</c> 包</b>，消费方一律可选注入
/// （<c>IImageEditor? editor = null</c>），没加载时为 null，退化为「本部署不支持编辑」。
/// 这与 <see cref="Documents.IDocumentConverter"/> 是同一个模式，理由也相同：
/// 想在自己的业务里遮挡一张图的模块（存储、工单、审核）<b>不该因此被拉进一个图像库的依赖闭包</b>。
/// 这里还多一条理由 —— 默认实现背后的 ImageSharp 自 v4 起需要商业授权，
/// 契约留在核心，换实现就不必改调用方。
/// </para>
/// <para>
/// <b>为什么这该是框架能力，而不是每个应用自己写。</b>
/// 一旦某类业务规定「原件不得离开受控环境」（举报、医疗影像、未成年人相关材料），
/// 「下载到本地用图像软件处理完再传回来」这条路就被堵死了，
/// 而人工判断哪一块该抹掉是无法自动化的 —— 于是编辑必须发生在服务端，
/// 否则经手人只剩两个选择：把原件原样交出去，或者什么都不交。
/// </para>
/// <para>
/// ★ <b>本接口只做像素级操作，不做「哪里该遮」的判断。</b>识别出画面里哪一块指向了谁，
/// 是人的工作（或另一套模型的工作），不是这里的。
/// </para>
/// </remarks>
public interface IImageEditor
{
    /// <summary>
    /// 这个文件名的扩展名此刻编辑得动。
    /// </summary>
    /// <param name="fileName">带扩展名的文件名。</param>
    /// <remarks>
    /// 按扩展名先答一次，让调用方能在界面上决定要不要给出「编辑」入口，
    /// 而不是等用户点下去才拿到一个失败。
    /// </remarks>
    bool CanEdit(string fileName);

    /// <summary>
    /// 按 <paramref name="request"/> 编辑图像，返回新的图像字节。
    /// </summary>
    /// <param name="source">源图字节。</param>
    /// <param name="sourceFileName">源图文件名（用于判定格式）。</param>
    /// <param name="request">编辑指令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>编辑后的图像字节与它的实际格式。</returns>
    /// <remarks>
    /// <b>不修改入参，也不写任何文件</b>：产物往哪存是调用方的决定。
    /// 源图无法解码、坐标越界、格式不支持一律抛异常，不返回一份「尽力而为」的产物 ——
    /// 一份没遮干净的图比一个错误危险得多。
    /// </remarks>
    Task<EditedImage> EditAsync(
        byte[] source,
        string sourceFileName,
        ImageEditRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一次图像编辑的产物。
/// </summary>
/// <param name="Content">图像字节。</param>
/// <param name="Format">实际输出格式。</param>
/// <param name="Width">输出宽度（像素）。</param>
/// <param name="Height">输出高度（像素）。</param>
public readonly record struct EditedImage(
    byte[] Content,
    ImageOutputFormat Format,
    int Width,
    int Height)
{
    /// <summary>
    /// 输出格式对应的 MIME 类型。
    /// </summary>
    public string ContentType => Format switch
    {
        ImageOutputFormat.Png => "image/png",
        ImageOutputFormat.WebP => "image/webp",
        _ => "image/jpeg"
    };

    /// <summary>
    /// 输出格式对应的扩展名（含点）。
    /// </summary>
    public string Extension => Format switch
    {
        ImageOutputFormat.Png => ".png",
        ImageOutputFormat.WebP => ".webp",
        _ => ".jpg"
    };
}
