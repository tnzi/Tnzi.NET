namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 定长记录字段写入工具（EFT 文件组装）
/// </summary>
internal static class EftFieldWriter
{
    /// <summary>
    /// 左对齐文本，右补空格（超长截断）。
    /// 先剥除嵌入的控制字符（换行/回车/制表等，替换为空格）：记录以 \n 连接且严格按位解析，
    /// 数据字段（PayeeName=Vendor.Name / 解密账号明文 / OriginatorName）内的 \n/\r 会把一条定宽记录
    /// 截成两行、错位其后所有字段 → 整文件被 ODFI 拒收，而长度不变式（<see cref="Fixed"/>）测不出（\n 单字符）。
    /// </summary>
    public static string Text(string? value, int width)
    {
        var v = Sanitize(value);
        if (v.Length > width)
            v = v[..width];
        return v.PadRight(width);
    }

    /// <summary>
    /// 写入账号字段：超长 <b>fail-fast</b> 而不是截断。
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="Text"/> 的差别就是这一条，而它是本文件里后果最重的一条。
    /// 截断一个自由文本字段（收款人名、摘要）只是难看；截断一个账号得到的是
    /// <b>另一个语法合法的账号</b> —— 银行按它处理，钱要么退回、要么进了别人的户头，
    /// 而录入、装批、生成三步全程 200。
    /// 与 <c>OriginatorId</c> 超 9 位的既有 fail-fast（<see cref="NachaFileBuilder"/>）对称：
    /// 出款方那侧宁可拒绝也不静默截断，收款方这侧没有理由更宽松。
    /// <para>
    /// 正常情况下录入时就被拦下了（见 <c>BankNumberHelper.MaxAccountNumberLength</c>），
    /// 这里是兜底 —— 存量档案、或绕过服务层写入的行仍可能超长。
    /// </para>
    /// </remarks>
    /// <param name="value">账号明文。</param>
    /// <param name="width">目标字段宽度。</param>
    /// <param name="subject">出错时指名是谁的账号（收款人名 / "the originating account"），
    /// 账号本身不进错误消息。</param>
    public static string AccountField(string? value, int width, string subject)
    {
        var v = Sanitize(value);
        if (v.Length > width)
            throw new BusinessException(
                $"The bank account number for {subject} is {v.Length} characters and does not fit the {width}-character account field. "
                + "A truncated account number can address a different account; correct the account number on file instead.");
        return v.PadRight(width);
    }

    /// <summary>把控制字符（含 \r\n\t 与其它不可打印字符）折叠为空格后去除首尾空白。</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(buffer).Trim();
    }

    /// <summary>右对齐数值，左补零（超长保留低位）。</summary>
    public static string Num(long value, int width)
    {
        var s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Length > width)
            s = s[^width..];
        return s.PadLeft(width, '0');
    }

    /// <summary>抽取数字并左补零到定宽（超长截断高位保留低位）。</summary>
    public static string Digits(string? value, int width)
    {
        var v = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (v.Length > width)
            v = v[^width..];
        return v.PadLeft(width, '0');
    }

    /// <summary>金额转分（无小数）。负数 fail-fast。</summary>
    /// <remarks>
    /// ★ 原先取 <c>Math.Abs</c>。NACHA 与 CPA-005 在本实现里都是**只出 credit** 的格式，
    /// 定宽金额字段里没有符号位 —— 于是一笔负额付款会被静默翻正，账上记的是收回一笔钱，
    /// 银行收到的却是付出同样一笔钱，两边金额一模一样、方向相反，对账时对得上。
    /// 框架当前的付款路径（草稿校验 + 过账校验）都要求金额为正，所以这条是兜底：
    /// 真要出现负额，正确的答案是拒绝出文件，而不是替它决定符号。
    /// </remarks>
    public static long Cents(decimal amount)
    {
        if (amount < 0)
            throw new BusinessException("A credit-only EFT file cannot carry a negative amount.");
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 写出一个金额字段（分）：装不下就 <b>fail-fast</b>，绝不丢高位。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="Num"/> 超长时保留的是<b>低位</b>（那对循环序号、trace 号是对的），
    /// 而对金额意味着一笔 100,000,000.00 的付款在 10 位字段里变成 0.00 ——
    /// 数字仍然合法、文件仍然定长、每一步仍然 200，只是金额少了一个数量级。
    /// 与账号超长同一类失效（见 <see cref="AccountField"/>），处理方式也必须一样。
    /// NACHA 的明细金额 10 位（上限 99,999,999.99）、合计 12 位；
    /// CPA-005 明细 10 位、合计 14 位 —— 上限来自格式，不是本框架的选择。
    /// </remarks>
    /// <param name="cents">金额（分，非负）。</param>
    /// <param name="width">目标字段宽度。</param>
    /// <param name="subject">出错时指名是哪一笔（收款人名 / "the batch total"）。</param>
    public static string Amount(long cents, int width, string subject)
    {
        var s = cents.ToString(CultureInfo.InvariantCulture);
        if (s.Length > width)
            throw new BusinessException(
                $"The amount for {subject} does not fit the {width}-digit amount field; "
                + "split the payment across smaller amounts or use a format with a wider field.");
        return s.PadLeft(width, '0');
    }

    /// <summary>空白填充。</summary>
    public static string Spaces(int width) => new(' ', width);

    /// <summary>CPA-005 儒略日期（0YYDDD，6 位）。</summary>
    public static string Julian(DateTime date) => $"0{date:yy}{date.DayOfYear:D3}";

    /// <summary>校验记录长度（组装不变量，越界抛以暴露布局错误）。</summary>
    public static string Fixed(string record, int width)
    {
        if (record.Length != width)
            throw new BusinessException($"EFT record length {record.Length} does not match the required {width}.");
        return record;
    }
}
