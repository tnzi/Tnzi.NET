namespace Tnzi.Signing.Tests;

/// <summary>
/// 假 stamper 用的确定性字宽模型：<see cref="Documents.Services.IPdfStamper.MeasureTextWidth"/> 的替身。
/// </summary>
/// <remarks>
/// 刻意让字宽<b>随字符而变</b>（大写比小写宽、W/M 更宽、CJK 一个全角）：如果排版代码又退回去按字符数估，
/// 等宽模型下它照样会过，而这个模型下全大写的一行会被量出越界。数值取自常见无衬线字体的量级，不必精确。
/// </remarks>
internal static class TestTextMetrics
{
    public static double Width(string text, double fontSize, bool bold = false)
    {
        var em = 0d;
        foreach (var c in text)
        {
            em += c switch
            {
                ' ' => 0.28d,
                'W' or 'M' or '@' => 0.95d,
                >= 'A' and <= 'Z' => 0.72d,
                >= '0' and <= '9' => 0.56d,
                >= 'a' and <= 'z' => 0.5d,
                >= '⺀' => 1d,
                _ => 0.6d,
            };
        }

        return em * (fontSize > 0 ? fontSize : 10d) * (bold ? 1.08d : 1d);
    }
}
