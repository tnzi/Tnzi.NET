namespace Tnzi.Finance.Tests;

/// <summary>
/// 路由号 / 机构号 / transit 号只认 ASCII 数字 0-9。
/// </summary>
/// <remarks>
/// <c>char.IsDigit</c> 认全部 Unicode 十进制数字：中文输入法全角状态下敲的「１２３４５」能存进档案、
/// 装进批次，几天后才在生成时失败（CPA-005 一条不点名的 400，NACHA 的 <c>long.Parse</c> 直接 500），
/// 白纸支票的 MICR 行还会印出全角字形。录入与生成必须同一判据，与账号、发起方编号的既有规则一致。
/// </remarks>
public class BankNumberHelperTests
{
    [Theory]
    [InlineData("００３", "12345")]
    [InlineData("003", "１２３４５")]
    [InlineData("٠٠٣", "12345")]
    public void CanadianRoutingWithNonAsciiDigits_IsRejected(string institution, string transit)
    {
        var result = BankNumberHelper.ValidateRouting(BankNumberScheme.CaEft, null, institution, transit);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Theory]
    [InlineData("０２１０００٠٢١")]
    [InlineData("୦୨୧୦୦୦୦୨୧")]
    public void UsRoutingWithNonAsciiDigits_IsRejected(string routing)
    {
        BankNumberHelper.ValidateRouting(BankNumberScheme.UsAba, routing, null, null).Succeeded.ShouldBeFalse();
        BankNumberHelper.IsValidAbaChecksum(routing).ShouldBeFalse();
    }

    [Fact]
    public void NonAsciiDigits_DoNotCountAsTransferRouting()
    {
        BankNumberHelper.HasTransferRouting(BankNumberScheme.CaEft, null, "００３", "１２３４５").ShouldBeFalse();
        BankNumberHelper.HasTransferRouting(BankNumberScheme.UsAba, "୦୨୧୦୦୦୦୨୧", null, null).ShouldBeFalse();
    }

    /// <summary>对照：同样的号用 ASCII 数字写，照常通过。</summary>
    [Fact]
    public void AsciiDigits_AreAccepted()
    {
        BankNumberHelper.ValidateRouting(BankNumberScheme.CaEft, null, "003", "12345").Succeeded.ShouldBeTrue();
        BankNumberHelper.ValidateRouting(BankNumberScheme.UsAba, "021000021", null, null).Succeeded.ShouldBeTrue();
        BankNumberHelper.HasTransferRouting(BankNumberScheme.CaEft, null, "003", "12345").ShouldBeTrue();
        BankNumberHelper.HasTransferRouting(BankNumberScheme.UsAba, "021000021", null, null).ShouldBeTrue();
    }

    /// <summary>
    /// 存量的全角路由号（早于录入校验写进库的）在 NACHA 生成时点名收款方拒绝，而不是 <c>long.Parse</c> 抛出的 500。
    /// </summary>
    [Fact]
    public void Nacha_WithALegacyNonAsciiPayeeRouting_FailsNamingThePayee()
    {
        var request = new EftComposeRequest
        {
            Format = EftFileFormat.Nacha,
            Currency = "USD",
            EffectiveDate = new DateTime(2026, 7, 15),
            CreationTime = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc),
            FileCreationNumber = 1,
            OriginatorId = "123456789",
            OriginatorName = "ACME CORP",
            OriginatorRoutingNumber = "021000021",
            Entries =
            {
                new EftComposeEntry
                {
                    PayeeName = "Globex", RoutingNumber = "୦୨୧୦୦୦୦୨୧", AccountNumber = "1234567",
                    AccountType = BankAccountType.Checking, Amount = 10m
                }
            }
        };

        var ex = Should.Throw<BusinessException>(() => NachaFileBuilder.Build(request));
        ex.Message.ShouldContain("Globex");
    }
}
