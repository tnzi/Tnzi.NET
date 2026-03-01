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
