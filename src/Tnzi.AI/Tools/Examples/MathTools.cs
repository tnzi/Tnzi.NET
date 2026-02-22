
namespace Tnzi.AI.Tools.Examples;

/// <summary>
/// 数学计算工具组 - 提供数学计算相关的 AI 工具函数
/// </summary>
[AIToolGroup("math", "数学工具", "提供数学计算和统计分析功能")]
public class MathTools : IAIToolProvider
{
    private readonly ILogger<MathTools> _logger;

    public MathTools(ILogger<MathTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 计算表达式
    /// </summary>
    [AIFunction("calculate", "计算数学表达式")]
    public Task<object> CalculateAsync(
        [AIParameter("expression", "数学表达式，如 '2 + 2'、'10 * 5'")]
        string expression)
    {
        _logger.LogDebug("Calculating expression: {Expression}", expression);

        try
        {
            // 简单的表达式计算（实际应该使用更安全的表达式解析器）
            var dataTable = new System.Data.DataTable();
            var result = dataTable.Compute(expression, null);
            return Task.FromResult<object>(new { result = Convert.ToDouble(result) });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 计算平均值
    /// </summary>
    [AIFunction("calculate_average", "计算数字列表的平均值")]
    public Task<object> CalculateAverageAsync(
        [AIParameter("numbers", "数字列表")]
        List<double> numbers)
    {
        if (numbers == null || numbers.Count == 0)
        {
            return Task.FromResult<object>(new { error = "Numbers list cannot be empty" });
        }

        var average = numbers.Average();
        return Task.FromResult<object>(new { average, count = numbers.Count });
    }
}