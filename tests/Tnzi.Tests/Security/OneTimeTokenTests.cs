namespace Tnzi.Tests.Security;

/// <summary>
/// 一次性凭据原语。
/// </summary>
/// <remarks>
/// 这些令牌用在「那一端没有登录、没有账号」的链路上（签署链接、注册邀请、领取码），
/// <b>持有这串字符就是全部凭据</b>，所以令牌的随机性与「库里不存明文」这两件事
/// 就是整条链路的全部安全性。
/// </remarks>
public class OneTimeTokenTests
{
    [Fact]
    public void Create_ProducesUniqueTokens()
    {
        // 可猜的序列等于任何人都能翻出别人那一条。
        var tokens = Enumerable.Range(0, 500).Select(_ => OneTimeToken.Create()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Create_Returns256BitsAndIsUrlSafe()
    {
        var token = OneTimeToken.Create();

        // 32 字节 base64 去填充 = 43 个字符。
        Assert.Equal(43, token.Length);

        // 进 URL 就不能带 + / =：它们要么被转义、要么被中间设备改写，
        // 而一条被改写过的链接是打不开的。
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void Create_AcceptsMoreEntropy()
    {
        Assert.Equal(86, OneTimeToken.Create(64).Length);
    }

    [Fact]
    public void Create_RejectsEntropyBelowTheFloor()
    {
        // 低于 128 位的令牌可被穷举。与其让调用方以为自己有防护，不如拒绝签发。
        Assert.Throws<ArgumentOutOfRangeException>(() => OneTimeToken.Create(8));
    }

    [Fact]
    public void Hash_IsDeterministicSoLookupByHashWorks()
    {
        // 查找路径比对的是哈希，所以同一输入必须恒得同一输出 ——
        // 这正是这里刻意不加盐的原因（加盐就查不了了）。
        var token = OneTimeToken.Create();

        Assert.Equal(OneTimeToken.Hash(token), OneTimeToken.Hash(token));
    }

    [Fact]
    public void Hash_DiffersPerToken()
    {
        Assert.NotEqual(
            OneTimeToken.Hash(OneTimeToken.Create()),
            OneTimeToken.Hash(OneTimeToken.Create()));
    }

    [Fact]
    public void Hash_IsLowercaseHexOfTheDeclaredLength()
    {
        var hash = OneTimeToken.Hash(OneTimeToken.Create());

        // 建表的列宽按 HashLength 取，所以这个常量必须与实际输出对得上。
        Assert.Equal(OneTimeToken.HashLength, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.All(hash, c => Assert.True(Uri.IsHexDigit(c)));
    }

    [Fact]
    public void Hash_DoesNotContainTheToken()
    {
        // ★ 这条看起来平凡，钉的却是整条链路的前提：库里那一列不能是明文，
        //   否则一份泄漏的备份就等同于一叠可用的链接。
        var token = OneTimeToken.Create();

        Assert.DoesNotContain(token, OneTimeToken.Hash(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_RejectsBlankToken(string blank)
    {
        // 空串会哈希出一个完全合法的值，于是「没有令牌」变成一条能被查到的记录。
        Assert.Throws<ArgumentException>(() => OneTimeToken.Hash(blank));
    }

    [Fact]
    public void MatchesHash_AcceptsTheIssuedTokenAndRejectsOthers()
    {
        var token = OneTimeToken.Create();
        var hash = OneTimeToken.Hash(token);

        Assert.True(OneTimeToken.MatchesHash(token, hash));
        Assert.False(OneTimeToken.MatchesHash(OneTimeToken.Create(), hash));
    }

    [Theory]
    [InlineData(null, "abc")]
    [InlineData("abc", null)]
    [InlineData("", "abc")]
    [InlineData("abc", "   ")]
    public void MatchesHash_ReturnsFalseForBlankSideInsteadOfThrowing(string? token, string? storedHash)
    {
        // 草稿阶段的记录哈希列是 null。让它抛异常，调用方就得在每个比对点先判空，
        // 而漏判的那一处会以 500 的形态出现在一条本该安静返回「不匹配」的路径上。
        Assert.False(OneTimeToken.MatchesHash(token, storedHash));
    }
}
