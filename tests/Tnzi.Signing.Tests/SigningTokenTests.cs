namespace Tnzi.Signing.Tests;

/// <summary>
/// 一次性签署链接的令牌。
/// </summary>
/// <remarks>
/// 链接那一端没有登录、没有账号，<b>持有这条链接就是全部凭据</b>，所以令牌的随机性与
/// "库里不存明文"这两件事就是这条链路的全部安全性。
/// </remarks>
public class SigningTokenTests
{
    [Fact]
    public void Each_token_is_unique()
    {
        // 可猜的序列等于任何人都能翻出别人的签署页。
        var tokens = Enumerable.Range(0, 500).Select(_ => SigningToken.Create()).ToList();

        tokens.Distinct().Count().ShouldBe(tokens.Count);
    }

    [Fact]
    public void A_token_carries_256_bits_of_entropy_and_is_url_safe()
    {
        var token = SigningToken.Create();

        // 32 字节 base64 去填充 = 43 个字符。
        token.Length.ShouldBe(43);
        // 进 URL 就不能带 + / =：它们要么被转义、要么被中间设备改写，
        // 而一条被改写过的签署链接是打不开的。
        token.ShouldNotContain("+");
        token.ShouldNotContain("/");
        token.ShouldNotContain("=");
    }

    [Fact]
    public void Hashing_is_deterministic_so_lookup_by_hash_works()
    {
        // 查找路径比对的是哈希，所以同一输入必须恒得同一输出 ——
        // 这正是这里刻意不加盐的原因（加盐就查不了了）。
        var token = SigningToken.Create();

        SigningToken.Hash(token).ShouldBe(SigningToken.Hash(token));
    }

    [Fact]
    public void Different_tokens_hash_differently()
    {
        SigningToken.Hash(SigningToken.Create())
            .ShouldNotBe(SigningToken.Hash(SigningToken.Create()));
    }

    [Fact]
    public void A_hash_is_lowercase_hex_of_the_right_length()
    {
        var hash = SigningToken.Hash(SigningToken.Create());

        // SHA-256 = 32 字节 = 64 个十六进制字符；列宽按这个定的。
        hash.Length.ShouldBe(64);
        hash.ShouldBe(hash.ToLowerInvariant());
        hash.ShouldAllBe(c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void The_hash_does_not_contain_the_token()
    {
        // ★ 这条看起来平凡，钉的却是整条链路的前提：库里那一列不能是明文，
        //   否则一份泄漏的备份就等同于一叠可用的签署链接。
        var token = SigningToken.Create();

        SigningToken.Hash(token).ShouldNotContain(token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hashing_a_blank_token_is_rejected(string blank)
    {
        // 空串会哈希出一个完全合法的值，于是"没有令牌"变成一条能被查到的记录。
        Should.Throw<ArgumentException>(() => SigningToken.Hash(blank));
    }
}
