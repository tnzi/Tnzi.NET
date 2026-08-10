namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 工资单计算钩子（公式表达不了的规则；default 空实现，按 <see cref="Order"/> 升序执行）
/// </summary>
/// <remarks>
/// 计算器为每张 payslip 环绕调用：<see cref="BeforeCalculateAsync"/> 在按序求值前
/// （可注入/调整变量），<see cref="AfterCalculateAsync"/> 在行求值后（可追加调整行或否决）。
/// 任一钩子返回失败 Result = 该 payslip 计算失败（记 CalculationError，不炸整批）。
/// country pack 经 <c>IEnumerable&lt;IPayslipCalculationHook&gt;</c> 注入自身钩子。
/// </remarks>
public interface IPayslipCalculationHook
{
    /// <summary>执行顺序（升序）</summary>
    int Order => 0;

    /// <summary>
    /// 本钩子会在 <see cref="BeforeCalculateAsync"/> 注入的变量名（大写规范形，与组件 Code 同口径）
    /// </summary>
    /// <remarks>
    /// 声明过的名字会并进**结构保存期**的公式变量白名单。★不声明就注入的变量，公式引用不到它
    /// ——结构存不进库（400）。这是刻意的：判据必须是静态可知的**声明**，否则「拼错变量名要报错」
    /// 就退化成只有跑批时才发现。
    /// <para>
    /// 值随 (辖区, 生效日) 变化不影响这里——白名单只关心**名字**，值在每张 payslip 求值前由
    /// <see cref="BeforeCalculateAsync"/> 现算。这正是 country pack 注入法定标量
    /// （费率 / 免征额 / 年度上限）的通路。
    /// </para>
    /// <para>
    /// ⚠️ 与组件 Code 撞名会被组件保存端拒绝：同一个名字不能既是注入标量、又是某个结构行的
    /// 计算结果——按序求值时后者会覆盖前者，引用它的公式读到哪个值取决于行序，是一条静默错账的路子。
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> ProvidedVariables => [];

    /// <summary>按序求值前（变量已就绪，行未生成）</summary>
    Task<Result> BeforeCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    /// <summary>按序求值后（行已生成，合计未落定；可追加行或否决）</summary>
    Task<Result> AfterCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());
}

/// <summary>
/// 工资单计算上下文（在钩子间共享同一张 payslip 的可变计算状态）
/// </summary>
public class PayslipCalculationContext
{
    public PayslipCalculationContext(PayRun payRun, Employee employee, SalaryStructure structure, decimal baseAmount)
    {
        PayRun = Check.NotNull(payRun);
        Employee = Check.NotNull(employee);
        Structure = Check.NotNull(structure);
        BaseAmount = baseAmount;
    }

    /// <summary>所属发薪批次</summary>
    public PayRun PayRun { get; }

    /// <summary>员工</summary>
    public Employee Employee { get; }

    /// <summary>薪资结构</summary>
    public SalaryStructure Structure { get; }

    /// <summary>基薪（公式变量 BASE）</summary>
    public decimal BaseAmount { get; }

    /// <summary>
    /// 变量表（内置变量 + 已算组件按 Code；大写 Ordinal 键，可变，供钩子注入/调整）
    /// </summary>
    public Dictionary<string, decimal> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>员工扩展属性（Attr()/AttrText() 数据源）</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已计算的工资单行（按序号；可变，钩子可追加调整行——合计在钩子后从此列表落定）
    /// </summary>
    public List<PayslipLine> Lines { get; } = [];
}
