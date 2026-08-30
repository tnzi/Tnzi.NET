using ZXing;

namespace Tnzi.Imaging.Services;

/// <summary>
/// 把一块 8 位灰度像素交给 ZXing。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>手写而不引 <c>ZXing.ImageSharp.Bindings</c></b>：那个绑定包对 ImageSharp 的版本下限
/// 由它自己决定，而本框架把 ImageSharp <b>锁在 3.1.x</b>（v4 起需要商业授权，见
/// <c>Tnzi.Imaging.csproj</c> 的说明）。让一个绑定包握着那条线的走向，
/// 意味着它哪天升到 v4，框架要么跟着违约要么再也升不动 ——
/// 而它替我们做的事只有这十几行：把像素抄进一个字节数组。
/// </para>
/// <para>
/// 继承 <see cref="BaseLuminanceSource"/> 而不是直接实现 <see cref="LuminanceSource"/>：
/// 裁切、旋转、反相都由基类按 <see cref="CreateLuminanceSource"/> 派生出来，
/// 而解码器的 <c>AutoRotate</c> / <c>TryInverted</c> 正是靠这几个操作工作的 ——
/// 不给它们实现，倒置和反相的码就静默读不出来。
/// </para>
/// </remarks>
internal sealed class BitmapLuminanceSource : BaseLuminanceSource
{
    /// <summary>用一块灰度像素初始化。</summary>
    /// <param name="luminances">逐行排列的 8 位灰度值，长度必须是 <paramref name="width"/> × <paramref name="height"/>。</param>
    /// <param name="width">宽度（像素）。</param>
    /// <param name="height">高度（像素）。</param>
    public BitmapLuminanceSource(byte[] luminances, int width, int height)
        : base(luminances, width, height)
    {
    }

    /// <inheritdoc />
    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        => new BitmapLuminanceSource(newLuminances, width, height);
}
