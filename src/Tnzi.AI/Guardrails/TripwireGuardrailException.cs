namespace Tnzi.AI.Guardrails;

/// <summary>
/// Tripwire 异常 - 任何 guardrail 抛出此异常将立即中止所有并行执行的 guardrail
/// </summary>
[ExperimentalApi(Reason = "Tripwire guardrails are in preview")]
public class TripwireGuardrailException : TnziException
{
    /// <summary>
    /// 触发 tripwire 的 Guardrail 名称
    /// </summary>
    public string GuardrailName { get; }

    /// <summary>
    /// 初始化 TripwireGuardrailException
    /// </summary>
    /// <param name="guardrailName">触发 tripwire 的 Guardrail 名称</param>
    /// <param name="reason">触发原因</param>
    public TripwireGuardrailException(string guardrailName, string reason)
        : base(ErrorCodes.GuardrailTripwire, reason)
    {
        GuardrailName = Check.NotNullOrEmpty(guardrailName);
    }
}
