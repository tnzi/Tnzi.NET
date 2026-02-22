
namespace Tnzi.AI.Tools.Examples;

/// <summary>
/// 文本处理工具组 - 提供文本处理相关的 AI 工具函数
/// </summary>
[AIToolGroup("text", "文本工具", "提供文本处理和转换功能")]
public class TextTools : IAIToolProvider
{
    private readonly ILogger<TextTools> _logger;

    public TextTools(ILogger<TextTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 统计文本信息
    /// </summary>
    [AIFunction("text_statistics", "统计文本的字数、字符数等信息")]
    public Task<object> GetTextStatisticsAsync(
        [AIParameter("text", "要统计的文本")]
        string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<object>(new { error = "Text cannot be empty" });
        }

        return Task.FromResult<object>(new
        {
            character_count = text.Length,
            word_count = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length,
            line_count = text.Split('\n').Length
        });
    }

    /// <summary>
    /// 转换文本大小写
    /// </summary>
    [AIFunction("convert_case", "转换文本大小写")]
    public Task<string> ConvertCaseAsync(
        [AIParameter("text", "要转换的文本")]
        string text,
        [AIParameter("case", "目标大小写：upper, lower, title")]
        string targetCase)
    {
        return targetCase.ToLower() switch
        {
            "upper" => Task.FromResult(text.ToUpper()),
            "lower" => Task.FromResult(text.ToLower()),
            "title" => Task.FromResult(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower())),
            _ => Task.FromResult(text)
        };
    }
}