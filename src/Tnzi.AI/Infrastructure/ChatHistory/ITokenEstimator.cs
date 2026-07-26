
namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// Token 估算器接口 - 用于估算文本的 Token 数量
/// </summary>
/// <remarks>
/// 默认实现使用启发式算法（区分 ASCII 和 CJK 字符的 token 比率）。
/// 用户可注册自定义实现（如集成 tiktoken）以获得更精确的估算。
/// </remarks>
public interface ITokenEstimator
{
    /// <summary>
    /// 估算文本的 Token 数量
    /// </summary>
    /// <param name="text">待估算的文本</param>
    /// <param name="baseOverhead">基础开销（系统提示等固定开销，默认 500）</param>
    /// <returns>估算的 Token 数量</returns>
    int Estimate(string text, int baseOverhead = 500);
}

/// <summary>
/// 默认 Token 估算器 - 基于字符类型的启发式算法
/// </summary>
/// <remarks>
/// ASCII 字符约 4 个字符 = 1 token，CJK 字符约 1 个字符 = 2 token。
/// 这是粗略估算，实际 Token 数量取决于模型使用的分词器。
/// </remarks>
public class HeuristicTokenEstimator : ITokenEstimator
{
    public int Estimate(string text, int baseOverhead = 500)
    {
        if (string.IsNullOrEmpty(text))
        {
            return baseOverhead;
        }

        var tokens = 0;
        foreach (var c in text)
        {
            // ASCII: ~4 chars/token → count 1 per char; Non-ASCII (CJK): ~2 tokens/char → count 8
            tokens += c < 128 ? 1 : 8;
        }
        return tokens / 4 + baseOverhead;
    }
}
