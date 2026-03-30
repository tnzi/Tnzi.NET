namespace Tnzi.Imaging.Dtos;

/// <summary>
/// 滑动验证码生成结果
/// </summary>
public class SlidingCaptchaDto
{
    /// <summary>
    /// 验证令牌（一次性）
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 背景图片（含缺口），Base64 编码
    /// </summary>
    public string BackgroundImage { get; set; } = string.Empty;

    /// <summary>
    /// 拼图块图片，Base64 编码
    /// </summary>
    public string PuzzlePiece { get; set; } = string.Empty;

    /// <summary>
    /// 拼图块 Y 坐标位置
    /// </summary>
    public int PieceY { get; set; }
}

/// <summary>
/// 滑动验证码验证结果
/// </summary>
public class SlidingCaptchaVerifyResult
{
    /// <summary>
    /// 是否验证通过
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 验证消息
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// 滑动验证码验证请求
/// </summary>
public class SlidingCaptchaVerifyRequest
{
    /// <summary>
    /// 验证令牌
    /// </summary>
    public string Token { get; set; } = null!;

    /// <summary>
    /// 用户滑动的 X 坐标
    /// </summary>
    public int X { get; set; }
}
