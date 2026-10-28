namespace Tnzi.AI.Services;

/// <summary>
/// 成本计算服务接口 - 根据模型成本率计算 Token 使用的美元成本
/// </summary>
public interface ICostCalculator
{
    /// <summary>
    /// 计算指定模型的 Token 使用成本（美元）
    /// </summary>
    /// <returns>成本值（美元），无匹配成本率时返回 null</returns>
    decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens);

    /// <summary>
    /// 计算成本，缓存命中的输入按 <c>CachedInputCostPer1MTokens</c> 计价。
    /// </summary>
    /// <param name="provider">提供商名</param>
    /// <param name="model">模型名</param>
    /// <param name="inputTokens">输入 Token 总数（含缓存命中，MEAI 的口径）</param>
    /// <param name="outputTokens">输出 Token 数</param>
    /// <param name="cachedInputTokens">其中从缓存读取的输入 Token 数</param>
    /// <returns>成本值（美元），无匹配成本率时返回 null</returns>
    /// <remarks>默认实现不区分缓存，与四参数版本相同，既有实现无需改动。</remarks>
    decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens, int cachedInputTokens)
        => CalculateCost(provider, model, inputTokens, outputTokens);

    /// <summary>
    /// 计算成本，缓存读取与缓存写入各按各的价目计。
    /// </summary>
    /// <param name="provider">提供商名</param>
    /// <param name="model">模型名</param>
    /// <param name="inputTokens">输入 Token 总数（含缓存读取与缓存写入，MEAI 的口径）</param>
    /// <param name="outputTokens">输出 Token 数</param>
    /// <param name="cachedInputTokens">其中从缓存读取的输入 Token 数</param>
    /// <param name="cacheWriteTokens">其中写入缓存的输入 Token 数</param>
    /// <returns>成本值（美元），无匹配成本率时返回 null</returns>
    /// <remarks>默认实现不区分缓存写入，与五参数版本相同，既有实现无需改动。</remarks>
    decimal? CalculateCost(string provider, string model, long inputTokens, long outputTokens, long cachedInputTokens, long cacheWriteTokens)
        => CalculateCost(provider, model, ClampToInt(inputTokens), ClampToInt(outputTokens), ClampToInt(cachedInputTokens));

    /// <summary>超出 int 的计数按 int.MaxValue 计（只有旧的整型重载才会用到）。</summary>
    private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
