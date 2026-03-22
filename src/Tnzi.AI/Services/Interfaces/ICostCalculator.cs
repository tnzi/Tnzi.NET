namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 成本计算服务接口 — 根据模型成本率计算 Token 使用的美元成本
/// </summary>
public interface ICostCalculator
{
    /// <summary>
    /// 计算指定模型的 Token 使用成本（美元）
    /// </summary>
    /// <returns>成本值（美元），无匹配成本率时返回 null</returns>
    decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens);
}
