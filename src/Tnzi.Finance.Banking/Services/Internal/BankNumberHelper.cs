namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行路由号校验与账号掩码的纯静态工具
/// </summary>
/// <remarks>
/// US ABA：9 位数字 + mod-10 校验位（3·7·1 加权）；CA EFT：机构号 3 位 + transit 号 5 位。
/// 掩码保留尾 4 位，其余以 <c>*</c> 表示，供列表展示（明文永不回 UI）。
/// </remarks>
internal static class BankNumberHelper
{
    /// <summary>
    /// 按账号方案校验路由字段。路由字段全空视为合法（允许仅登记名称/账号的档案）。
    /// </summary>
    public static Result ValidateRouting(BankNumberScheme scheme, string? routingNumber, string? institutionNumber, string? transitNumber)
    {
        switch (scheme)
        {
            case BankNumberScheme.UsAba:
                if (string.IsNullOrWhiteSpace(routingNumber))
                    return Result.Success();
                var routing = routingNumber.Trim();
                if (routing.Length != 9 || !routing.All(char.IsDigit))
                    return Result.Failure("A US ABA routing number must be exactly 9 digits.", 400);
                if (!IsValidAbaChecksum(routing))
                    return Result.Failure("The US ABA routing number failed its mod-10 checksum.", 400);
                return Result.Success();

            case BankNumberScheme.CaEft:
                if (string.IsNullOrWhiteSpace(institutionNumber) && string.IsNullOrWhiteSpace(transitNumber))
                    return Result.Success();
                if (string.IsNullOrWhiteSpace(institutionNumber) || institutionNumber.Trim().Length != 3 || !institutionNumber.Trim().All(char.IsDigit))
                    return Result.Failure("A Canadian institution number must be exactly 3 digits.", 400);
                if (string.IsNullOrWhiteSpace(transitNumber) || transitNumber.Trim().Length != 5 || !transitNumber.Trim().All(char.IsDigit))
                    return Result.Failure("A Canadian transit number must be exactly 5 digits.", 400);
                return Result.Success();

            default:
                return Result.Failure("Unknown bank number scheme.", 400);
        }
    }

    /// <summary>
    /// 该方案下账号可占的最大字符数 —— 即它在对应 EFT 文件里那个定宽字段的宽度。
    /// </summary>
    /// <remarks>
    /// 上限来自文件格式而不是数据库列宽：超过这个长度的账号写不进文件，
    /// 而定宽写入器一旦截断，得到的是一个语法合法、可能属于别人的账号。
    /// 校验前移到录入是为了让操作员在<b>还看得见自己刚敲的那串数字</b>时被拦下，
    /// 而不是几天后装批生成时才收到一条与他无关的报错。
    /// </remarks>
    public static int MaxAccountNumberLength(BankNumberScheme scheme) => scheme switch
    {
        BankNumberScheme.UsAba => NachaFileBuilder.AccountNumberWidth,
        BankNumberScheme.CaEft => Cpa005FileBuilder.AccountNumberWidth,
        // 未知方案取两者中更严的那个：宁可拒绝一个也许合法的账号，也不放行一个会被截断的。
        _ => Math.Min(NachaFileBuilder.AccountNumberWidth, Cpa005FileBuilder.AccountNumberWidth)
    };

    /// <summary>按方案校验账号长度（明文，调用方须先 Trim）。</summary>
    public static Result ValidateAccountNumberLength(BankNumberScheme scheme, string accountNumber)
    {
        Check.NotNull(accountNumber);
        var max = MaxAccountNumberLength(scheme);
        if (accountNumber.Length > max)
            return Result.Failure(
                $"The account number is {accountNumber.Length} characters; a {scheme} account number cannot exceed {max} characters.", 400);
        return Result.Success();
    }

    /// <summary>
    /// 该方案下路由字段是否齐备到<b>足以寻址一笔电子转账</b>。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ValidateRouting"/> 刻意不同：档案允许只登记名称/账号（路由留空是合法的，
    /// 见那里的注释），但这样的档案<b>装不进 EFT 文件</b> —— 留空会被定宽写入器补成全零路由，
    /// 那是一份语法合法、指向不存在机构的报文。两个问题不同，答案也不该共用一个方法。
    /// </remarks>
    public static bool HasTransferRouting(BankNumberScheme scheme, string? routingNumber, string? institutionNumber, string? transitNumber)
        => scheme switch
        {
            BankNumberScheme.UsAba => DigitCount(routingNumber) == 9,
            BankNumberScheme.CaEft => DigitCount(institutionNumber) == 3 && DigitCount(transitNumber) == 5,
            _ => false
        };

    private static int DigitCount(string? value) => (value ?? string.Empty).Count(char.IsDigit);

    /// <summary>US ABA mod-10 校验位（3·7·1 加权和被 10 整除）</summary>
    public static bool IsValidAbaChecksum(string routing)
    {
        if (routing.Length != 9 || !routing.All(char.IsDigit))
            return false;

        var d = routing.Select(c => c - '0').ToArray();
        var sum = 3 * (d[0] + d[3] + d[6])
                + 7 * (d[1] + d[4] + d[7])
                + 1 * (d[2] + d[5] + d[8]);
        return sum % 10 == 0;
    }

    /// <summary>账号掩码：保留尾 4 位，前缀固定 4 星（不足 4 位全掩码）。
    /// 固定星号数而非按明文长度补星——后者会泄露账号实际位数（美国 ABA 账号长度本身即敏感信息）。</summary>
    public static string Mask(string accountNumber)
    {
        Check.NotNull(accountNumber);
        var trimmed = accountNumber.Trim();
        if (trimmed.Length <= 4)
            return new string('*', trimmed.Length);
        return "****" + trimmed[^4..];
    }
}
