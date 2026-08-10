namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 收集计算钩子**声明**会提供的外部变量名（规范化为大写 Ordinal 形，与组件 Code 同口径）。
/// </summary>
/// <remarks>
/// ★存在的理由：<see cref="IPayslipCalculationHook.BeforeCalculateAsync"/> 能往
/// <see cref="PayslipCalculationContext.Variables"/> 注入按 (辖区, 生效日) 变化的标量
/// （CPP_RATE / 基本免征额 / 年度最高供款…），但结构保存期的白名单只认
/// 「内置变量 ∪ 更早序号行的组件 Code」——注入得再对，也没有公式敢引用它，
/// 于是这个扩展点对它设计出来要服务的场景（country pack）基本不可用。
/// <para>
/// 判据是**声明**而不是注入：名字集合在保存那一刻仍然静态可知，所以
/// 「引用一个谁也不提供的变量名 → 400」这条静态检查的价值一分不丢。
/// </para>
/// </remarks>
internal static class PayrollExternalVariables
{
    /// <summary>无钩子声明时的共享空集（校验是每次保存都跑的热路径，不必每次分配）</summary>
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>收集全部已注册钩子声明的变量名（大写规范形，去重）</summary>
    internal static IReadOnlySet<string> Collect(IEnumerable<IPayslipCalculationHook> hooks)
    {
        Check.NotNull(hooks);

        HashSet<string>? names = null;
        foreach (var hook in hooks)
        {
            foreach (var name in hook.ProvidedVariables)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                names ??= new HashSet<string>(StringComparer.Ordinal);
                names.Add(name.Trim().ToUpperInvariant());
            }
        }

        return names ?? Empty;
    }
}
