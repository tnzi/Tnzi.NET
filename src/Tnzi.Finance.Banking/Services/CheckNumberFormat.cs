namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 支票号的定宽呈现规则（票面、MICR 串行号、以及消费应用自己的登记簿共用同一份）
/// </summary>
/// <remarks>
/// 支票号在库里是一个 <see cref="long"/>。直接 <c>ToString()</c> 的话，第 2 张支票在票面上
/// 印成 <c>2</c>、MICR 串行号也是一位数，而消费应用的银行登记簿里它会挤在一堆定长参考号中间。
/// <para>
/// ★ <b>这不是合规缺陷</b>：CPA Standard 006 §4.4.4 把串行号字段定为<b>变长</b>，并且说串行号
/// 本身「highly recommended but not mandatory」。补零是**可读性**问题 —— 对账时一列右对齐的
/// 等宽数字才扫得动。
/// </para>
/// <para>
/// ★ <b>本类是 public 的，这是它存在的理由之一</b>：消费应用要在自己的界面上把同一个支票号
/// 渲染成与纸上一模一样的样子。规则藏在渲染器里的话，每个消费方只能自己猜一遍补几位 ——
/// 猜错了不会有任何报错，只是屏幕上的号与纸上的号长得不一样。
/// </para>
/// <para>
/// ★ <b>永不截断</b>：位数是<b>下限</b>不是定长。号段涨过配置位数时印完整的号，
/// 而不是切掉高位 —— 切掉高位印出来的是**另一张支票的号**。
/// </para>
/// </remarks>
public static class CheckNumberFormat
{
    /// <summary>未配置时的位数。</summary>
    /// <remarks>
    /// 5 位覆盖到 99999，是北美商用支票本最常见的长度。要改就改配置，见
    /// <c>Finance:CheckNumberDigits</c>。
    /// </remarks>
    public const int DefaultDigits = 5;

    /// <summary>允许的最小位数。<b>1 = 不补零</b>（任何 ≥1 的号都至少一位），即旧行为的显式表达。</summary>
    public const int MinDigits = 1;

    /// <summary>允许的最大位数（MICR 串行号字段放不下更长的了，再长也读不进去）。</summary>
    public const int MaxDigits = 12;

    /// <summary>
    /// 把配置值归一到 <see cref="MinDigits"/>..<see cref="MaxDigits"/>；
    /// <see langword="null"/> 或越界 → <see cref="DefaultDigits"/>。
    /// </summary>
    /// <remarks>
    /// ★ 越界回退到默认值而不是 clamp 到边界：配置里写着 <c>0</c> 或 <c>99</c> 的人是搞错了，
    /// 而「悄悄给你一个边界值」与「按你写的办」在纸上看起来一样，都不会让人回头去看配置。
    /// 启动期的 <c>FinanceCheckOptionsValidator</c> 才是真正拦住它的地方，本方法只是运行期兜底
    /// （渲染请求可以由消费应用直接构造，那条路绕过了配置校验）。
    /// </remarks>
    public static int Normalize(int? digits)
        => digits is >= MinDigits and <= MaxDigits ? digits.Value : DefaultDigits;

    /// <summary>
    /// 按位数补零渲染支票号；<paramref name="digits"/> 为 <see langword="null"/> 时用默认位数。
    /// </summary>
    /// <remarks>
    /// 负号不该出现（支票号由 <c>CheckNumberAllocator</c> 从正数号段分配），真出现时原样印出来 ——
    /// 一个显然不对的号比一个被格式化得很正常的号容易被发现。
    /// </remarks>
    public static string Format(long checkNumber, int? digits = null)
        => checkNumber.ToString("D" + Normalize(digits).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
