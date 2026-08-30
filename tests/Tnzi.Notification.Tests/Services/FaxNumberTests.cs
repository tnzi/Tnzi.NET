namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="FaxNumber"/> 的归一化规则。
/// </summary>
/// <remarks>
/// ★ 这组用例值得写得比它看起来该有的更细：归一化错了**没有症状**。带前导 1 的北美号码
/// 会被网关照单全收，投递记录是成功的，那份传真只是永远不会到达。没有异常、没有退信、没有日志 ——
/// 唯一能提前发现它的地方就是这里。
/// </remarks>
public class FaxNumberTests
{
    /// <summary>
    /// ★ 全组里最要紧的一条：11 位、以 1 打头的北美号码必须去掉那个 1。
    /// </summary>
    [Theory]
    [InlineData("19055551234")]
    [InlineData("1 905 555 1234")]
    [InlineData("+1 (905) 555-1234")]
    [InlineData("1-905-555-1234")]
    [InlineData("+1.905.555.1234")]
    public void TryNormalize_DropsTheNanpLongDistancePrefix(string raw)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeTrue(error);

        normalized.ShouldBe("9055551234");
    }

    [Theory]
    [InlineData("9055551234")]
    [InlineData("(905) 555-1234")]
    [InlineData("905-555-1234")]
    [InlineData("  905 555 1234  ")]
    public void TryNormalize_KeepsATenDigitNanpNumberAsIs(string raw)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeTrue(error);

        normalized.ShouldBe("9055551234");
    }

    /// <summary>
    /// 国际号码按其数字原样通过 —— 砍前导 1 只在"正好 11 位"时成立。
    /// </summary>
    [Theory]
    [InlineData("+44 20 7946 0958", "442079460958")]      // 英国，12 位
    [InlineData("+86 10 1234 5678", "861012345678")]      // 中国，12 位
    [InlineData("+33 1 23 45 67 89", "33123456789")]      // 法国，11 位但不以 1 打头
    [InlineData("030/12345678", "03012345678")]           // 德国本地写法，斜杠是分隔符
    public void TryNormalize_PassesInternationalNumbersThroughAsDigits(string raw, string expected)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeTrue(error);

        normalized.ShouldBe(expected);
    }

    /// <summary>
    /// ★ 11 位的规则**只**在 11 位时成立。12 位以上以 1 打头的是别国号码，
    /// 砍掉那一位会把传真拨到另一个地方去 —— 同样是无症状的失败。
    /// </summary>
    [Theory]
    [InlineData("+1 905 555 1234 5", "190555512345")]
    [InlineData("1234567890123", "1234567890123")]
    public void TryNormalize_OnlyStripsTheLeadingOneWhenThereAreExactlyElevenDigits(string raw, string expected)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeTrue(error);

        normalized.ShouldBe(expected);
    }

    /// <summary>
    /// 10 位而以 1 打头的不是 NANP（区号不会以 0 或 1 开头），照国际号码原样通过。
    /// </summary>
    [Fact]
    public void TryNormalize_DoesNotStripALeadingOneFromATenDigitNumber()
    {
        FaxNumber.TryNormalize("1055551234", out var normalized, out _).ShouldBeTrue();

        normalized.ShouldBe("1055551234");
    }

    /// <summary>
    /// ★ 分机不能"顺手滤掉非数字"：那样 <c>9055551234 ext. 99</c> 会变成
    /// <c>905555123499</c> —— 语法合法、网关照收、石沉大海。宁可当场拒绝。
    /// </summary>
    [Theory]
    [InlineData("9055551234 ext. 99")]
    [InlineData("9055551234x99")]
    [InlineData("905-555-1234 #2")]
    [InlineData("905*555*1234")]
    [InlineData("905,555,1234")]
    public void TryNormalize_RejectsAnythingThatIsNotADigitOrASeparator(string raw)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeFalse();

        normalized.ShouldBeEmpty();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()-. ")]
    [InlineData("123")]
    [InlineData("123456")]
    public void TryNormalize_RejectsWhatCannotBeDialled(string? raw)
    {
        FaxNumber.TryNormalize(raw, out var normalized, out var error).ShouldBeFalse();

        normalized.ShouldBeEmpty();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryNormalize_RejectsMoreThanFifteenDigits()
    {
        FaxNumber.TryNormalize("9055551234567890", out _, out var error).ShouldBeFalse();

        error!.ShouldContain("15");
    }

    [Fact]
    public void TryNormalize_AcceptsExactlyTheBoundaryLengths()
    {
        FaxNumber.TryNormalize(new string('9', FaxNumber.MinDigits), out _, out _).ShouldBeTrue();
        FaxNumber.TryNormalize(new string('9', FaxNumber.MaxDigits), out _, out _).ShouldBeTrue();
    }

    /// <summary>失败原因必须带上原始输入，否则批量发送时没人知道是哪一条不合规。</summary>
    [Fact]
    public void TryNormalize_NamesTheOffendingNumberInTheError()
    {
        FaxNumber.TryNormalize("905-555-1234 ext 4", out _, out var error).ShouldBeFalse();

        error!.ShouldContain("905-555-1234 ext 4");
    }

    [Fact]
    public void Normalize_ThrowsOnAnUndialableNumber()
    {
        Should.Throw<ArgumentException>(() => FaxNumber.Normalize("nope"));
    }

    [Fact]
    public void Normalize_ReturnsTheSameResultAsTryNormalize()
    {
        FaxNumber.Normalize("+1 (905) 555-1234").ShouldBe("9055551234");
    }
}
