namespace Tnzi.Imaging;

/// <summary>
/// 一次图像编辑：在源图上遮挡、模糊、裁切、缩放，产出一份新的图像字节。
/// </summary>
/// <remarks>
/// <para>
/// <b>全部坐标都相对源图</b>（<see cref="NormalizedRect"/>，0-1，左上角原点），
/// 与裁切与否无关。调用方在自己看到的那张图上框选，不需要换算 ——
/// 「先裁了再算遮挡该往哪挪」是这类接口最常见的错误来源。
/// </para>
/// <para>
/// <b>执行顺序固定</b>：遮挡 → 模糊 → 裁切 → 缩放。顺序不可配：
/// 先缩放再遮挡会让遮挡落在插值过的像素上，边缘可能残留原始信息。
/// </para>
/// <para>
/// <b>产物与原图之间是单向的。</b>实现方必须重绘像素并重新编码，
/// 而不是往图上叠一层不透明矩形 —— 后者在位图里看似等价，
/// 但一旦产物格式支持图层或元数据（如带 alpha 的 PNG 被再次处理），被遮的内容就还在。
/// </para>
/// </remarks>
public class ImageEditRequest
{
    /// <summary>
    /// 要遮挡的区域：用不透明实色覆盖，<b>被覆盖的像素不可恢复</b>。
    /// </summary>
    /// <remarks>
    /// 需要真正抹掉的东西（门牌号、车牌、聊天界面里的账号）用这个，不要用
    /// <see cref="BlurRegions"/> —— 理由写在那一项上。
    /// </remarks>
    public IReadOnlyList<NormalizedRect> Redactions { get; set; } = [];

    /// <summary>
    /// 遮挡块的填充色，十六进制 <c>#RRGGBB</c>；默认黑色。
    /// </summary>
    public string? RedactionColor { get; set; }

    /// <summary>
    /// 要模糊的区域。
    /// </summary>
    /// <remarks>
    /// ★ <b>模糊不是遮挡的替代品。</b>高斯模糊是可逆程度很高的变换：
    /// 对文字、车牌这类取值空间有限的内容，枚举候选再模糊一遍去比对就能还原，
    /// 这在公开案例里反复发生过。
    /// <para>
    /// 它的正当用途是<b>降低意外暴露</b>：先呈现一张模糊的首屏，由查看者主动确认后
    /// 再完整渲染 —— 保护的是查看者，不是画面里的人。要保护画面里的人，用
    /// <see cref="Redactions"/>。
    /// </para>
    /// </remarks>
    public IReadOnlyList<NormalizedRect> BlurRegions { get; set; } = [];

    /// <summary>
    /// 高斯模糊强度（sigma），默认 <c>12</c>。取值越大越糊。
    /// </summary>
    public float BlurSigma { get; set; } = 12f;

    /// <summary>
    /// 裁切区域；为 <c>null</c> 表示不裁。
    /// </summary>
    /// <remarks>
    /// 裁切在遮挡与模糊之后执行，因此裁掉的部分即使原本要遮挡也不影响结果。
    /// </remarks>
    public NormalizedRect? Crop { get; set; }

    /// <summary>
    /// 输出的最大宽度（像素）；<c>0</c> 表示不限。
    /// </summary>
    /// <remarks>与 <see cref="MaxHeight"/> 一起构成等比缩放的包围盒，只缩不放。</remarks>
    public int MaxWidth { get; set; }

    /// <summary>
    /// 输出的最大高度（像素）；<c>0</c> 表示不限。
    /// </summary>
    public int MaxHeight { get; set; }

    /// <summary>
    /// 输出格式；为 <c>null</c> 时沿用源图格式。
    /// </summary>
    public ImageOutputFormat? OutputFormat { get; set; }

    /// <summary>
    /// JPEG / WebP 的编码质量（1-100），默认 <c>85</c>；PNG 忽略此项。
    /// </summary>
    public int Quality { get; set; } = 85;
}

/// <summary>
/// 图像编辑的输出格式。
/// </summary>
public enum ImageOutputFormat
{
    /// <summary>JPEG。有损，体积小，不支持透明。</summary>
    Jpeg = 0,

    /// <summary>PNG。无损，支持透明。</summary>
    Png = 1,

    /// <summary>WebP。</summary>
    WebP = 2
}
