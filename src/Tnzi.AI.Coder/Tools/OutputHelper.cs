namespace Tnzi.AI.Coder;

/// <summary>
/// 工具输出截断辅助方法
/// </summary>
internal static class OutputHelper
{
    /// <summary>
    /// 截断输出到最大字符数，超出部分用省略号提示
    /// </summary>
    public static string Truncate(string output, long maxSize)
    {
        if (output.Length <= maxSize)
        {
            return output;
        }

        var truncated = output[..(int)maxSize];
        return truncated + $"\n... (truncated, {output.Length} total chars)";
    }
}
