namespace Tnzi.Imaging.Options;

/// <summary>
/// 滑动验证码配置选项
/// </summary>
public class SlidingCaptchaOptions
{
    /// <summary>
    /// 背景图片宽度（默认300）
    /// </summary>
    public int Width { get; set; } = 300;

    /// <summary>
    /// 背景图片高度（默认150）
    /// </summary>
    public int Height { get; set; } = 150;

    /// <summary>
    /// 拼图块大小（默认50）
    /// </summary>
    public int PieceSize { get; set; } = 50;

    /// <summary>
    /// 验证容差（默认5像素）
    /// </summary>
    public int Tolerance { get; set; } = 5;

    /// <summary>
    /// 验证码过期时间（分钟，默认5）
    /// </summary>
    public int ExpirationMinutes { get; set; } = 5;
}
