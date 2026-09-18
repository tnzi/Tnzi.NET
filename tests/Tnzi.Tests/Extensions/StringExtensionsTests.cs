namespace Tnzi.Tests.Extensions;

/// <summary>
/// StringExtensions 测试类
/// </summary>
public class StringExtensionsTests
{
    #region 空值检查测试

    [Theory]
    [InlineData("test", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNotNullOrEmpty_ReturnsCorrectResult(string? input, bool expected)
    {
        // Act
        var result = input.IsNotNullOrEmpty();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("test", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNotNullOrWhiteSpace_ReturnsCorrectResult(string? input, bool expected)
    {
        // Act
        var result = input.IsNotNullOrWhiteSpace();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region 哈希算法测试

    [Fact]
    public void ToMd5_WithValidString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        const string expectedHash = "098f6bcd4621d373cade4e832627b4f6";

        // Act
        var result = input.ToMd5();

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void ToMd5_WithEmptyString_ReturnsEmptyString()
    {
        // Act
        var result = string.Empty.ToMd5();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToSha256_WithValidString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        const string expectedHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        // Act
        var result = input.ToSha256();

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void ToSha512_WithValidString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";

        // Act
        var result = input.ToSha512();

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(128, result.Length); // SHA512 produces 64 bytes = 128 hex characters
    }

    #endregion

    #region 字符串处理测试

    [Theory]
    [InlineData("///test", '/', "test")]
    [InlineData("test", '/', "test")]
    [InlineData("", '/', "")]
    public void TrimStart_WithChar_TrimsCorrectly(string input, char trimChar, string expected)
    {
        // Act
        var result = input.TrimStart(trimChar);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("test///", '/', "test")]
    [InlineData("test", '/', "test")]
    [InlineData("", '/', "")]
    public void TrimEnd_WithChar_TrimsCorrectly(string input, char trimChar, string expected)
    {
        // Act
        var result = input.TrimEnd(trimChar);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("TestString", "testString")]
    [InlineData("test", "test")]
    [InlineData("T", "t")]
    [InlineData("", "")]
    public void ToCamelCase_ConvertsCorrectly(string input, string expected)
    {
        // Act
        var result = input.ToCamelCase();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("testString", "TestString")]
    [InlineData("Test", "Test")]
    [InlineData("t", "T")]
    [InlineData("", "")]
    public void ToPascalCase_ConvertsCorrectly(string input, string expected)
    {
        // Act
        var result = input.ToPascalCase();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("TestString", "test_string")]
    [InlineData("testString", "test_string")]
    [InlineData("Test", "test")]
    [InlineData("", "")]
    public void ToSnakeCase_ConvertsCorrectly(string input, string expected)
    {
        // Act
        var result = input.ToSnakeCase();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("This is a long string", 10, "...", "This is a ...")]
    [InlineData("Short", 10, "...", "Short")]
    [InlineData("Exactly10!", 10, "...", "Exactly10!")]
    public void Truncate_TruncatesCorrectly(string input, int maxLength, string suffix, string expected)
    {
        // Act
        var result = input.Truncate(maxLength, suffix);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("<p>Hello</p>", "Hello")]
    [InlineData("<div><span>Test</span></div>", "Test")]
    [InlineData("No tags", "No tags")]
    [InlineData("", "")]
    public void RemoveHtmlTags_RemovesTagsCorrectly(string input, string expected)
    {
        // Act
        var result = input.RemoveHtmlTags();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("<p>Hello</p>", "Hello")]
    [InlineData("No tags", "No tags")]
    [InlineData("", "")]
    [InlineData(null, "")]
    // Adjacent blocks must not glue into one word - this is why RemoveHtmlTags is not enough.
    [InlineData("<p>Dear Ann</p><p>Your file is open.</p>", "Dear Ann Your file is open.")]
    [InlineData("<b>a</b><b>b</b>", "a b")]
    // Entities are decoded, and the indentation HTML carries is collapsed away.
    [InlineData("Ben &amp; Co.&nbsp;&copy; open", "Ben & Co. © open")]
    [InlineData("<div>\n   spaced\n\n   out\n</div>", "spaced out")]
    // An attribute containing '>' must not end the tag early.
    [InlineData("<a title=\"a > b\" href=\"#\">link</a>", "link")]
    public void HtmlToPlainText_ProducesReadableText(string? input, string expected)
    {
        Assert.Equal(expected, input.HtmlToPlainText());
    }

    [Fact]
    public void HtmlToPlainText_DiffersFromRemoveHtmlTags_OnWordBoundariesAndEntities()
    {
        const string html = "<p>a</p><p>b</p>&amp;";

        Assert.Equal("ab&amp;", html.RemoveHtmlTags());
        Assert.Equal("a b &", html.HtmlToPlainText());
    }

    #endregion

    #region 验证测试

    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("invalid.email", false)]
    [InlineData("@example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEmail_ValidatesCorrectly(string? input, bool expected)
    {
        // Act
        var result = input?.IsEmail() ?? false;

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsEmail_OverLength_ReturnsFalse()
    {
        // RFC 5321 上限 254；超长直接拒绝，不进正则
        var local = new string('a', 250);
        Assert.False((local + "@example.com").IsEmail());
        Assert.True((new string('a', 240) + "@example.com").IsEmail());
    }

    [Fact]
    public void IsEmail_PathologicalDots_ReturnsFalseWithinBudget()
    {
        var input = "a@" + string.Concat(Enumerable.Repeat("a.", 120)) + "!";

        var sw = Stopwatch.StartNew();
        var result = input.IsEmail();
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds}ms");
    }

    [Theory]
    [InlineData("(415) 555-2671")]
    [InlineData("415-555-2671")]
    [InlineData("415.555.2671")]
    [InlineData("4155552671")]
    [InlineData("5552671")]
    [InlineData("+1 415.555.2671 x12")]
    [InlineData("+1 (415) 555-2671 ext 123")]
    [InlineData("+1-415-555-2671")]
    [InlineData("415 555 2671 extension 4")]
    [InlineData("(415) 555-2671#12")]
    public void IsPhoneNumber_ValidShapes_StillMatch(string input)
    {
        Assert.True(input.IsPhoneNumber());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("115-555-2671")]
    [InlineData("415-555-267")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("415-555-2671 x")]
    public void IsPhoneNumber_InvalidShapes_Rejected(string input)
    {
        Assert.False(input.IsPhoneNumber());
    }

    [Theory]
    [InlineData("13800138000", true)]
    [InlineData("23800138000", false)]
    [InlineData("1380013800", false)]
    public void IsPhoneNumber_LooseMode_ElevenDigitsStartingWithOne(string input, bool expected)
    {
        Assert.Equal(expected, input.IsPhoneNumber(isRestrict: false));
    }

    [Fact]
    public void IsPhoneNumber_PathologicalWhitespace_ReturnsFalseWithinBudget()
    {
        // 三层相邻的可选空白量词：无长度闸门与线性引擎时，失败输入按 N^2 到 N^3 回溯
        var input = "+1" + new string(' ', 20000) + "!";

        var sw = Stopwatch.StartNew();
        var result = input.IsPhoneNumber();
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void IsPhoneNumber_OverLength_ReturnsFalse()
    {
        Assert.False(("+1 (415) 555-2671 extension " + new string('1', 40)).IsPhoneNumber());
        Assert.True("+1 (415) 555-2671 extension 12345".IsPhoneNumber());
    }



    [Theory]
    [InlineData("test123", @"^\w+$", true)]
    [InlineData("test@123", @"^\w+$", false)]
    [InlineData("", @"^\w+$", false)]
    public void IsMatch_MatchesCorrectly(string? input, string pattern, bool expected)
    {
        // Act
        var result = input?.IsMatch(pattern) ?? false;

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region 正则表达式测试

    [Theory]
    [InlineData("test123", @"\d+", "123")]
    [InlineData("no numbers", @"\d+", null)]
    [InlineData("", @"\d+", null)]
    public void Match_MatchesCorrectly(string? input, string pattern, string? expected)
    {
        // Act
        var result = input?.Match(pattern);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Matches_ReturnsAllMatches()
    {
        // Arrange
        const string input = "test123abc456";
        const string pattern = @"\d+";

        // Act
        var results = input.Matches(pattern).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains("123", results);
        Assert.Contains("456", results);
    }

    [Theory]
    [InlineData("test123", @"\d+", "XXX", "testXXX")]
    [InlineData("no numbers", @"\d+", "XXX", "no numbers")]
    public void ReplaceRegex_ReplacesCorrectly(string input, string pattern, string replacement, string expected)
    {
        // Act
        var result = input.ReplaceRegex(pattern, replacement);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region 字节转换测试

    [Fact]
    public void ToBytes_ConvertsCorrectly()
    {
        // Arrange
        const string input = "test";

        // Act
        var result = input.ToBytes();

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(4, result.Length); // "test" = 4 bytes in UTF8
    }

    [Fact]
    public void ToBytes_WithEmptyString_ReturnsEmptyArray()
    {
        // Act
        var result = string.Empty.ToBytes();

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
