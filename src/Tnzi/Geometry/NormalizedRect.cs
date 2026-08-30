namespace Tnzi.Geometry;

/// <summary>
/// 归一化矩形：取值 0-1，<b>原点在左上角，Y 轴向下</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是框架内所有「在一张页面或一幅图上标位置」的统一坐标形态</b> ——
/// PDF 定位与盖章（<c>Tnzi.Documents</c>）、签名落笔点（<c>Tnzi.Signing</c>）、
/// 图像遮挡与裁切（<see cref="Tnzi.Imaging.ImageEditRequest"/>）用的是同一个类型，
/// 「在这里找到 -&gt; 在这里画」中间零换算。
/// </para>
/// <para>
/// 刻意与各自的原生坐标解耦（PDF 是左下角原点、单位 point；图像是像素）：
/// <list type="bullet">
/// <item>与前端 overlay 同一套坐标系，乘以渲染宽高即得 CSS 像素位置，
/// 呈现端不需要知道原始尺寸；</item>
/// <item>缩放、不同纸张尺寸、不同分辨率下坐标依然成立 ——
/// 协调员在一张缩略图上框出来的位置，落到原图上仍然是同一块。</item>
/// </list>
/// </para>
/// <para>
/// ★ <b>读要翻 Y，写不翻 Y</b>：从 PDF 原生坐标读进来时要换算，
/// 往外画时按本坐标系直接给。这条铁律的换算实现在 <c>Tnzi.Documents</c> 内部。
/// </para>
/// </remarks>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>右边界（X + Width）。</summary>
    public double Right => X + Width;

    /// <summary>下边界（Y + Height）。</summary>
    public double Bottom => Y + Height;

    /// <summary>面积（用于在多个候选框里挑主定位框）。</summary>
    public double Area => Width * Height;

    /// <summary>空矩形（四个分量均为 0）。</summary>
    public static NormalizedRect Empty => default;

    /// <summary>
    /// 四个分量是否都落在 0-1 内，且宽高为正。
    /// </summary>
    /// <remarks>
    /// 越界的矩形几乎总是坐标系搞错了（拿像素当归一化、或者 Y 轴没翻），
    /// 而它的表现是「遮挡画在了图外」这种安静的失效 —— 该在入口处就拒绝。
    /// </remarks>
    public bool IsWithinUnitSquare()
        => Width > 0 && Height > 0
           && X >= 0 && Y >= 0
           && Right <= 1 && Bottom <= 1;
}
