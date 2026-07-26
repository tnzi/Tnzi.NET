namespace Tnzi.System.Tests.Services;

/// <summary>
/// AesSettingEncryptor 单元测试
/// </summary>
public class AesSettingEncryptorTests
{
    private readonly AesSettingEncryptor _encryptor;
    private readonly string _testKey;

    public AesSettingEncryptorTests()
    {
        // 生成一个有效的 32 字节 AES-256 密钥
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        _testKey = Convert.ToBase64String(keyBytes);

        var options = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = _testKey
        });

        _encryptor = new AesSettingEncryptor(options);
    }

    [Fact]
    public void Encrypt_Should_Return_Base64_String()
    {
        // Act
        var encrypted = _encryptor.Encrypt("hello world");

        // Assert
        encrypted.ShouldNotBeNullOrWhiteSpace();
        // 应该是有效的 Base64
        Should.NotThrow(() => Convert.FromBase64String(encrypted));
    }

    [Fact]
    public void Decrypt_Should_Return_Original_Value()
    {
        // Arrange
        var plainText = "my-secret-api-key-12345";

        // Act
        var encrypted = _encryptor.Encrypt(plainText);
        var decrypted = _encryptor.Decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(plainText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("short password")]
    [InlineData("a-very-long-connection-string-Server=myserver;Database=mydb;User=admin;Password=P@ssw0rd!;Encrypt=true;TrustServerCertificate=false")]
    [InlineData("special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?")]
    [InlineData("unicode: 中文测试 日本語テスト 한국어테스트")]
    public void Encrypt_Decrypt_Roundtrip_Various_Inputs(string plainText)
    {
        // Act
        var encrypted = _encryptor.Encrypt(plainText);
        var decrypted = _encryptor.Decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(plainText);
    }

    [Fact]
    public void Encrypt_Same_Input_Should_Produce_Different_CipherText()
    {
        // Arrange
        var plainText = "same-input";

        // Act - 多次加密同一明文
        var encrypted1 = _encryptor.Encrypt(plainText);
        var encrypted2 = _encryptor.Encrypt(plainText);

        // Assert - 由于随机 nonce，密文应不同
        encrypted1.ShouldNotBe(encrypted2);

        // 但解密结果应相同
        _encryptor.Decrypt(encrypted1).ShouldBe(plainText);
        _encryptor.Decrypt(encrypted2).ShouldBe(plainText);
    }

    [Fact]
    public void Decrypt_Invalid_Base64_Should_Throw_BusinessException()
    {
        // Act & Assert
        var ex = Should.Throw<BusinessException>(() => _encryptor.Decrypt("not-valid-base64!!!"));
        ex.Message.ShouldContain("Invalid encrypted value format");
    }

    [Fact]
    public void Decrypt_Too_Short_Data_Should_Throw_BusinessException()
    {
        // Arrange - 有效 Base64 但数据太短（少于 nonce + tag = 28 字节）
        var shortData = Convert.ToBase64String(new byte[10]);

        // Act & Assert
        var ex = Should.Throw<BusinessException>(() => _encryptor.Decrypt(shortData));
        ex.Message.ShouldContain("data too short");
    }

    [Fact]
    public void Decrypt_Tampered_Data_Should_Throw_BusinessException()
    {
        // Arrange
        var encrypted = _encryptor.Encrypt("secret");
        var bytes = Convert.FromBase64String(encrypted);

        // 篡改密文的最后一个字节（tag 区域）
        bytes[^1] = (byte)(bytes[^1] ^ 0xFF);
        var tampered = Convert.ToBase64String(bytes);

        // Act & Assert
        var ex = Should.Throw<BusinessException>(() => _encryptor.Decrypt(tampered));
        ex.Message.ShouldContain("Failed to decrypt");
    }

    [Fact]
    public void Decrypt_With_Wrong_Key_Should_Throw_BusinessException()
    {
        // Arrange - 用一个密钥加密
        var encrypted = _encryptor.Encrypt("secret");

        // 用另一个密钥创建新的解密器
        var otherKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(otherKeyBytes);
        var otherOptions = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = Convert.ToBase64String(otherKeyBytes)
        });
        var otherEncryptor = new AesSettingEncryptor(otherOptions);

        // Act & Assert
        var ex = Should.Throw<BusinessException>(() => otherEncryptor.Decrypt(encrypted));
        ex.Message.ShouldContain("Failed to decrypt");
    }

    [Fact]
    public void Constructor_Should_Throw_When_Key_Not_Configured()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = null
        });

        // Act & Assert
        Should.Throw<ConfigurationException>(() => new AesSettingEncryptor(options));
    }

    [Fact]
    public void Constructor_Should_Throw_When_Key_Wrong_Length()
    {
        // Arrange - 16 字节密钥（AES-128，不是 AES-256）
        var shortKey = Convert.ToBase64String(new byte[16]);
        var options = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = shortKey
        });

        // Act & Assert
        Should.Throw<ConfigurationException>(() => new AesSettingEncryptor(options));
    }
}

/// <summary>
/// SettingEncryptionOptions 验证器测试
/// </summary>
public class SettingEncryptionOptionsValidatorTests
{
    private readonly SettingEncryptionOptionsValidator _validator = new();

    [Fact]
    public void Validate_Disabled_Should_Pass()
    {
        // Arrange
        var options = new SettingEncryptionOptions { Enabled = false, EncryptionKey = null };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Enabled_With_Valid_Key_Should_Pass()
    {
        // Arrange
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var options = new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = Convert.ToBase64String(keyBytes)
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Enabled_Without_Key_Should_Fail()
    {
        // Arrange
        var options = new SettingEncryptionOptions { Enabled = true, EncryptionKey = null };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("EncryptionKey is required");
    }

    [Fact]
    public void Validate_Enabled_With_Invalid_Base64_Should_Fail()
    {
        // Arrange
        var options = new SettingEncryptionOptions { Enabled = true, EncryptionKey = "not-base64!!!" };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("valid Base64");
    }

    [Fact]
    public void Validate_Enabled_With_Wrong_Key_Length_Should_Fail()
    {
        // Arrange - 16 字节密钥，不是 32
        var options = new SettingEncryptionOptions
        {
            Enabled = true,
            EncryptionKey = Convert.ToBase64String(new byte[16])
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("32-byte");
    }
}
