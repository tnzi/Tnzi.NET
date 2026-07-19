namespace Tnzi.AI.Metadata;

/// <summary>
/// 澄清请求类型
/// </summary>
public enum ClarificationType
{
    /// <summary>缺少必要信息</summary>
    MissingInfo,

    /// <summary>需求描述模糊</summary>
    AmbiguousRequirement,

    /// <summary>方案选择</summary>
    ApproachChoice,

    /// <summary>风险确认</summary>
    RiskConfirmation,

    /// <summary>建议确认</summary>
    Suggestion
}
