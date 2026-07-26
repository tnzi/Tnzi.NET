
namespace Tnzi.Tests.Security;

/// <summary>
/// HashHelper 测试类
/// </summary>
public class HashHelperTests
{
    #region MD5 Tests

    [Fact]
    public void GetMd5_WithString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        const string expectedHash = "098f6bcd4621d373cade4e832627b4f6";

        // Act
        var result = HashHelper.GetMd5(input);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetMd5_WithNullString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetMd5((string)null!));
    }

    [Fact]
    public void GetMd5_WithEmptyString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetMd5(string.Empty));
    }

    [Fact]
    public void GetMd5_WithBytes_ReturnsCorrectHash()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("test");
        const string expectedHash = "098f6bcd4621d373cade4e832627b4f6";

        // Act
        var result = HashHelper.GetMd5(bytes);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetMd5_WithNullBytes_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => HashHelper.GetMd5((byte[])null!));
    }

    [Fact]
    public void GetMd5_WithEmptyBytes_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetMd5(Array.Empty<byte>()));
    }

    [Fact]
    public void GetMd5_WithStream_ReturnsCorrectHash()
    {
        // Arrange
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        const string expectedHash = "098f6bcd4621d373cade4e832627b4f6";

        // Act
        var result = HashHelper.GetMd5(stream);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetMd5_WithNullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => HashHelper.GetMd5((Stream)null!));
    }

    [Fact]
    public void GetMd5_WithStream_ResetsPosition()
    {
        // Arrange
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        stream.Position = 2; // 设置非零位置

        // Act
        HashHelper.GetMd5(stream);

        // Assert - 验证流位置被重置到开头
        Assert.Equal(4, stream.Position); // MD5 计算后位置应该在末尾
    }

    #endregion

    #region SHA1 Tests

    [Fact]
    public void GetSha1_WithString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        const string expectedHash = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3";

        // Act
        var result = HashHelper.GetSha1(input);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetSha1_WithNullString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetSha1(null!));
    }

    [Fact]
    public void GetSha1_WithEmptyString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetSha1(string.Empty));
    }

    [Fact]
    public void GetSha1_WithCustomEncoding_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        var encoding = Encoding.ASCII;

        // Act
        var result = HashHelper.GetSha1(input, encoding);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(40, result.Length); // SHA1 produces 20 bytes = 40 hex characters
    }

    #endregion

    #region SHA256 Tests

    [Fact]
    public void GetSha256_WithString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        const string expectedHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        // Act
        var result = HashHelper.GetSha256(input);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetSha256_WithNullString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetSha256((string)null!));
    }

    [Fact]
    public void GetSha256_WithBytes_ReturnsCorrectHash()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("test");
        const string expectedHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        // Act
        var result = HashHelper.GetSha256(bytes);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetSha256_WithNullBytes_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => HashHelper.GetSha256((byte[])null!));
    }

    [Fact]
    public void GetSha256_WithStream_ReturnsCorrectHash()
    {
        // Arrange
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        const string expectedHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        // Act
        var result = HashHelper.GetSha256(stream);

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void GetSha256_WithNullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => HashHelper.GetSha256((Stream)null!));
    }

    #endregion

    #region SHA512 Tests

    [Fact]
    public void GetSha512_WithString_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";

        // Act
        var result = HashHelper.GetSha512(input);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(128, result.Length); // SHA512 produces 64 bytes = 128 hex characters
    }

    [Fact]
    public void GetSha512_WithNullString_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => HashHelper.GetSha512(null!));
    }

    [Fact]
    public void GetSha512_WithCustomEncoding_ReturnsCorrectHash()
    {
        // Arrange
        const string input = "test";
        var encoding = Encoding.Unicode;

        // Act
        var result = HashHelper.GetSha512(input, encoding);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(128, result.Length);
    }

    #endregion

    #region Hash Consistency Tests

    [Fact]
    public void GetMd5_WithSameInput_ReturnsSameHash()
    {
        // Arrange
        const string input = "test";

        // Act
        var hash1 = HashHelper.GetMd5(input);
        var hash2 = HashHelper.GetMd5(input);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetSha256_WithDifferentInput_ReturnsDifferentHash()
    {
        // Arrange
        const string input1 = "test1";
        const string input2 = "test2";

        // Act
        var hash1 = HashHelper.GetSha256(input1);
        var hash2 = HashHelper.GetSha256(input2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GetMd5_WithLongString_ReturnsValidHash()
    {
        // Arrange
        var input = new string('a', 10000);

        // Act
        var result = HashHelper.GetMd5(input);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(32, result.Length); // MD5 always produces 32 hex characters
    }

    #endregion

    #region Password Hash Tests

    [Fact]
    public void VerifyPassword_WithMatchingPassword_ReturnsTrue()
    {
        var stored = HashHelper.HashPassword("correct horse battery staple");

        Assert.True(HashHelper.VerifyPassword("correct horse battery staple", stored));
        Assert.False(HashHelper.VerifyPassword("Correct horse battery staple", stored));
    }

    /// <summary>
    /// 摘要段为空的存储串必须一律拒绝。
    /// </summary>
    /// <remarks>
    /// 派生长度取自存储的摘要，摘要为空时两侧都是零长数组，而
    /// <c>FixedTimeEquals(空, 空)</c> 为真——任意口令都能通过。列被截断、手工改库、
    /// 或写了一半的哈希都会落到这个形状上，而口令门恰恰用在封账日这类地方。
    /// </remarks>
    [Theory]
    [InlineData("pbkdf2$sha256$210000$c2FsdA==$")]
    [InlineData("pbkdf2$sha256$210000$$")]
    public void VerifyPassword_WithDegenerateStoredHash_ReturnsFalse(string stored)
    {
        Assert.False(HashHelper.VerifyPassword("anything at all", stored));
        Assert.False(HashHelper.VerifyPassword("", stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2$sha512$210000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$sha256$210000$!!!$aGFzaA==")]
    public void VerifyPassword_WithUnusableStoredHash_ReturnsFalse(string? stored)
    {
        Assert.False(HashHelper.VerifyPassword("anything at all", stored));
    }

    #endregion
}
