namespace Tnzi.AI.Services;

/// <summary>
/// 一个模型收不收采样参数（<c>temperature</c> / <c>top_p</c> / <c>top_k</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 新一代 Claude 型号对带 <c>temperature</c> 的请求直接答 400（「<c>temperature</c> is deprecated for this model」），
/// 不是忽略它。所以「把一个配好的温度发出去」对这些型号不是无害的默认动作，而是让整次调用失败。
/// 发请求的地方在知道目标型号时问这里一句，不收的就把采样参数整组去掉：
/// 显式配置的温度对收它的型号照常生效，对不收的型号不再把请求弄坏。
/// </para>
/// <para>
/// 判据按型号名：<c>claude-{opus|sonnet|haiku}-{major}</c> 中 major ≥ 5，以及 <c>claude-fable-</c>（Claude 5 家族）。
/// 型号名前面带着路由前缀也认得（<c>anthropic/claude-sonnet-5-5</c>、<c>us.anthropic.claude-haiku-5-5</c>）。
/// 4.x 及更早的 Claude 与所有非 Claude 型号视为收（这是改动前的行为）。
/// 未逐个核实的型号（Claude 4.7 / 4.8、Mythos）刻意不列：默认已不发温度，只有显式配置了温度才会碰到，
/// 那时的 400 指向的是一条明确的配置，比悄悄吞掉一个有效的配置好查。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public static partial class ModelSamplingSupport
{
    /// <summary>
    /// 型号收不收采样参数。型号未知（null / 空）时答 true：不知道就不替调用方改请求。
    /// </summary>
    public static bool AcceptsSamplingParameters(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return true;
        }

        var index = model.IndexOf("claude-", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return true;
        }

        var name = model[index..];
        if (name.StartsWith("claude-fable-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = ClaudeMajorVersion().Match(name);
        return !match.Success
            || int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture) < 5;
    }

    [GeneratedRegex(@"^claude-(?:opus|sonnet|haiku)-(?<major>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeMajorVersion();
}
