namespace Tnzi.Documents.Models;

/// <summary>
/// 页面归一化矩形：取值 0-1，**原点在页面左上角，Y 轴向下**。
/// </summary>
/// <remarks>
/// 这是本包对外唯一的坐标形态，刻意与 PDF 原生坐标（左下角原点、单位 point）解耦：
/// <list type="bullet">
/// <item>与前端 pdf.js overlay 同一套坐标系，乘以渲染宽高即得 CSS 像素位置，呈现端不需要知道页面点数尺寸；</item>
/// <item><see cref="Services.IPdfInspector"/> 产出、<see cref="Services.IPdfStamper"/> 消费，
/// 两端共用同一类型，「找到标签 -&gt; 在标签处盖章」中间零换算；</item>
/// <item>页面缩放/不同纸张尺寸下坐标依然成立。</item>
/// </list>
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
}
