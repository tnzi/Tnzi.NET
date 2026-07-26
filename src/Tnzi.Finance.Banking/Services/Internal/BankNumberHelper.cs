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
