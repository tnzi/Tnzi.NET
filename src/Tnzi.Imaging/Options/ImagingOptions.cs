namespace Tnzi.Imaging.Options;

/// <summary>
/// Imaging 模块配置选项
/// </summary>
public class ImagingOptions
{
    /// <summary>
    /// 验证码图片配置
    /// </summary>
    public CaptchaOptions Captcha { get; set; } = new();

    /// <summary>
    /// 滑动验证码配置
    /// </summary>
    public SlidingCaptchaOptions SlidingCaptcha { get; set; } = new();

    /// <summary>
    /// 单张图片解码后的像素上限（宽 × 高，默认 4000 万，0 表示不限制）。
    /// </summary>
    /// <remarks>
    /// ★ 这是<b>闸门不是调优项</b>（同 <c>Tnzi.Documents</c> 的 <c>MaxPagePixels</c>）：
    /// 压缩字节数与解码后的内存没有关系，一个 200KB 的 PNG 可以声明 50000×50000、
    /// 解码要 10 TB —— 没有它，任何一个接受图片的入口都是拒绝服务入口。
    /// 设成 0 等于把那个入口重新打开，只应在完全可信的内部管道里这么做。
    /// </remarks>
    public long MaxDecodePixels { get; set; } = ImageDecodeGuard.DefaultMaxDecodePixels;
}

/// <summary>
/// 验证码图片外观配置
/// </summary>
public class CaptchaOptions
{
    /// <summary>
    /// 字体大小（默认20）
    /// </summary>
    public int FontSize { get; set; } = 20;

    /// <summary>
    /// 字体宽度（默认与FontSize相同）
    /// </summary>
    public int FontWidth { get; set; }

    /// <summary>
    /// 图片高度（0表示自动计算：FontSize + FontSize/2）
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 是否有边框
    /// </summary>
    public bool HasBorder { get; set; }

    /// <summary>
    /// 是否随机位置
    /// </summary>
    public bool RandomPosition { get; set; }

    /// <summary>
    /// 是否随机字体颜色
    /// </summary>
    public bool RandomColor { get; set; } = true;

    /// <summary>
    /// 是否随机倾斜字体
    /// </summary>
    public bool RandomItalic { get; set; }

    /// <summary>
    /// 随机干扰点百分比（0-100）
    /// </summary>
    public double RandomPointPercent { get; set; }

    /// <summary>
    /// 随机干扰线数量
    /// </summary>
    public int RandomLineCount { get; set; } = 2;

    /// <summary>
    /// 验证码默认过期时间（分钟）
    /// </summary>
    public int ExpireMinutes { get; set; } = 5;

    /// <summary>
    /// 验证码默认长度
    /// </summary>
    public int DefaultLength { get; set; } = 4;
}
