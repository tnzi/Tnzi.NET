namespace Tnzi.Identity.Tests;

/// <summary>
/// 登录 IP 允许列表的算术：解析操作员文本、判定命中。保存时的校验与登录时的判定都走这里，
/// 所以这组用例同时是两边的规格。
/// </summary>
public class SignInIpAllowListTests
{
    [Fact]
    public void ParseEntries_SplitsOnNewlinesCommasAndSemicolons_TrimsAndDropsBlanksAndComments()
    {
        const string text = " 203.0.113.5 \n# office\r\n10.0.0.0/8, 2001:db8::1 ;;\n\n";

        var entries = SignInIpAllowList.ParseEntries(text);

        Assert.Equal(["203.0.113.5", "10.0.0.0/8", "2001:db8::1"], entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n \r\n")]
    [InlineData("# only a comment")]
    public void ParseEntries_ReturnsEmptyForNothingUsable(string? text)
    {
        Assert.Empty(SignInIpAllowList.ParseEntries(text));
    }

    [Theory]
    [InlineData("203.0.113.5", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("192.168.1.0/24", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("::ffff:203.0.113.5", true)]
    [InlineData("203.0.113", false)]
    [InlineData("203.0.113.5/", false)]
    [InlineData("203.0.113.5/33", false)]
    [InlineData("office", false)]
    [InlineData("203.0.113.5 # inline comment", false)]
    public void IsValidEntry_AcceptsExactAddressesAndCidrOnly(string entry, bool expected)
    {
        Assert.Equal(expected, SignInIpAllowList.IsValidEntry(entry));
    }

    [Fact]
    public void FindInvalidEntries_ReportsEveryBadEntryInOrder()
    {
        var invalid = SignInIpAllowList.FindInvalidEntries("203.0.113.5\nnope\n10.0.0.0/8\nstill nope");

        Assert.Equal(["nope", "still nope"], invalid);
    }

    [Theory]
    [InlineData("203.0.113.5", "203.0.113.5", true)]
    [InlineData("203.0.113.6", "203.0.113.5", false)]
    [InlineData("192.168.1.77", "192.168.1.0/24", true)]
    [InlineData("192.168.2.1", "192.168.1.0/24", false)]
    [InlineData("2001:db8::42", "2001:db8::/32", true)]
    [InlineData("2001:db9::42", "2001:db8::/32", false)]
    [InlineData("2001:db8::1", "2001:db8::1", true)]
    public void IsAllowed_MatchesExactAddressesAndRanges(string client, string entry, bool expected)
    {
        Assert.Equal(expected, SignInIpAllowList.IsAllowed(client, [entry]));
    }

    /// <summary>
    /// 同一个客户端经不同代理跳数到达，可能是 <c>203.0.113.5</c> 也可能是 <c>::ffff:203.0.113.5</c>。
    /// 不折叠的话规则在一套部署上命中、在另一套上不命中，而症状是「密码错误」。
    /// </summary>
    [Theory]
    [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
    [InlineData("::ffff:192.168.1.77", "192.168.1.0/24")]
    [InlineData("203.0.113.5", "::ffff:203.0.113.5")]
    public void IsAllowed_TreatsIPv4MappedIPv6AsTheSameClient(string client, string entry)
    {
        Assert.True(SignInIpAllowList.IsAllowed(client, [entry]));
    }

    /// <summary>
    /// 原生 IPv6 客户端的低 32 位由它自己选（接口 ID）。<c>2001:db8::cb00:7105</c> 的低 32 位
    /// 正是 <c>203.0.113.5</c>；若按低 32 位折成 v4 去比，任何人都能凑出一个命中 v4 网段的 v6 地址。
    /// </summary>
    [Theory]
    [InlineData("2001:db8::1", "192.168.1.0/24")]
    [InlineData("2001:db8::cb00:7105", "203.0.113.0/24")]
    [InlineData("2001:db8::cb00:7105", "203.0.113.5/32")]
    [InlineData("::cb00:7105", "203.0.113.0/24")]
    [InlineData("2001:db8::cb00:7105", "::ffff:203.0.113.0/120")]
    public void IsAllowed_PureIPv6ClientNeverMatchesAnIPv4Range(string client, string entry)
    {
        Assert.False(SignInIpAllowList.IsAllowed(client, [entry]));
    }

    /// <summary>反向同理：IPv4 客户端不在任何原生 IPv6 网段里，只在 v4 映射网段里。</summary>
    [Theory]
    [InlineData("203.0.113.5", "::/0", false)]
    [InlineData("203.0.113.5", "::/96", false)]
    [InlineData("203.0.113.5", "2001:db8::/32", false)]
    [InlineData("203.0.113.5", "::ffff:203.0.113.0/120", true)]
    [InlineData("::ffff:203.0.113.5", "::ffff:203.0.113.0/120", true)]
    [InlineData("203.0.114.5", "::ffff:203.0.113.0/120", false)]
    public void IsAllowed_IPv4ClientOnlyMatchesIPv4OrMappedRanges(string client, string entry, bool expected)
    {
        Assert.Equal(expected, SignInIpAllowList.IsAllowed(client, [entry]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    public void IsAllowed_UnknownOrUnparseableClientNeverMatches(string? client)
    {
        Assert.False(SignInIpAllowList.IsAllowed(client, ["0.0.0.0/0", "::/0"]));
    }

    [Fact]
    public void IsAllowed_EmptyEntrySetNeverMatches()
    {
        Assert.False(SignInIpAllowList.IsAllowed("203.0.113.5", []));
    }

    [Fact]
    public void IsAllowed_SkipsMalformedEntriesInsteadOfThrowing()
    {
        Assert.True(SignInIpAllowList.IsAllowed("203.0.113.5", ["garbage", "999.1.1.1/8", "203.0.113.5"]));
        Assert.False(SignInIpAllowList.IsAllowed("203.0.113.5", ["garbage", "999.1.1.1/8"]));
    }
}
