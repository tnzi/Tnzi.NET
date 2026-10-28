namespace Tnzi.Identity.Tests;

/// <summary>
/// 2FA 挑战临时令牌的格式：随机部分 + 本次挑战提供的方式。
/// </summary>
public class TwoFactorChallengeTokenTests
{
    [Fact]
    public void ReadOffered_ReturnsExactlyTheMethodsTheChallengeOffered()
    {
        var token = TwoFactorChallengeToken.Create([TwoFactorType.Totp, TwoFactorType.Sms]);

        var offered = TwoFactorChallengeToken.ReadOffered(token);

        Assert.NotNull(offered);
        Assert.Equal([TwoFactorType.Sms, TwoFactorType.Totp], offered!.OrderBy(t => t));
    }

    [Fact]
    public void Create_IssuesADifferentRandomPartEveryTime()
    {
        var first = TwoFactorChallengeToken.Create([TwoFactorType.Email]);
        var second = TwoFactorChallengeToken.Create([TwoFactorType.Email]);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// 随机部分用 URL 安全的字母表：令牌由前端原样回传，<c>/</c> <c>+</c> <c>=</c> 进了 URL 要么被转义、要么被改写，
    /// 改写过的令牌按哈希查不到。随机部分不含分隔符 <c>.</c>，所以读方式列表不受影响。
    /// </summary>
    [Fact]
    public void Create_IssuesAUrlSafeToken_WhoseMethodsStillReadBack()
    {
        for (var i = 0; i < 64; i++)
        {
            var token = TwoFactorChallengeToken.Create([TwoFactorType.Email, TwoFactorType.Totp]);

            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('=', token);
            Assert.Equal([TwoFactorType.Email, TwoFactorType.Totp], TwoFactorChallengeToken.ReadOffered(token)!.OrderBy(t => t));
        }
    }

    /// <summary>没有可选方式的挑战不该发出：签发时就拒绝，而不是签出一枚第二步永远走不通的令牌。</summary>
    [Fact]
    public void Create_RefusesAChallengeThatOffersNothing()
        => Assert.Throws<ArgumentException>(() => TwoFactorChallengeToken.Create([]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator-at-all")]
    [InlineData("random.")]
    [InlineData(".3")]
    [InlineData("random.3-")]
    [InlineData("random.99")]
    [InlineData("random.+3")]
    [InlineData("random.three")]
    public void ReadOffered_AnswersNullForAnythingAChallengeDidNotIssue(string? token)
        => Assert.Null(TwoFactorChallengeToken.ReadOffered(token));
}
