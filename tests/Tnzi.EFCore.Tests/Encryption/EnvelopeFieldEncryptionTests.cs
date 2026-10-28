using Tnzi.Security;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 信封加密:每个值一把随机数据密钥,主密钥只封装数据密钥。
/// </summary>
/// <remarks>
/// 关注的是一旦破掉就会静默出错的性质:数据密钥不共用、封装密钥与载荷都绑定用途、
/// 篡改任何一段都解不开、轮换只换封装不动载荷。
/// </remarks>
public class EnvelopeFieldEncryptionTests
{
    private const string Purpose = "Demo.Connection.AccessToken";
    private const string OtherPurpose = "Demo.Connection.RefreshToken";

    private static string NewKey() => AesGcmHelper.GenerateKeyBase64();

    private static EnvelopeFieldEncryptor CreateEncryptor(
        out MutableFieldEncryptionOptionsMonitor monitor,
        string activeKeyId = "m1",
        params string[] keyIds)
    {
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            Mode = FieldEncryptionMode.Envelope,
            ActiveKeyId = activeKeyId,
            Keys = new Dictionary<string, string> { ["m1"] = NewKey() }
        };
        foreach (var id in keyIds)
        {
            options.Keys[id] = NewKey();
        }

        monitor = new MutableFieldEncryptionOptionsMonitor(options);
        return new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(monitor));
    }

    /// <summary>密文 = keyId : e1 : 封装的数据密钥 : 载荷。</summary>
    private static string[] Split(string ciphertext) => ciphertext.Split(':');

    private static string Join(params string[] parts) => string.Join(':', parts);

    [Fact]
    public void Roundtrip_RestoresOriginalValue()
    {
        var encryptor = CreateEncryptor(out _);

        var cipher = encryptor.Encrypt("ya29.a0-oauth-access-token", Purpose);

        Assert.Equal("ya29.a0-oauth-access-token", encryptor.Decrypt(cipher, Purpose));
    }

    [Theory]
    [InlineData("")]
    [InlineData("中文令牌与 emoji \U0001F511")]
    public void Roundtrip_HandlesEmptyAndMultiByteValues(string plaintext)
    {
        var encryptor = CreateEncryptor(out _);

        Assert.Equal(plaintext, encryptor.Decrypt(encryptor.Encrypt(plaintext, Purpose), Purpose));
    }

    [Fact]
    public void Ciphertext_CarriesTheWrappedKey_AndNoPlaintext()
    {
        var encryptor = CreateEncryptor(out _);

        var cipher = encryptor.Encrypt("super-secret-token", Purpose);
        var parts = Split(cipher);

        Assert.Equal(4, parts.Length);
        Assert.Equal("m1", parts[0]);
        Assert.Equal("e1", parts[1]);
        Assert.NotEmpty(parts[2]);
        Assert.NotEmpty(parts[3]);
        Assert.DoesNotContain("secret", cipher, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoValues_NeverShareADataKey()
    {
        // 同一明文、同一用途加密两次。若两个值共用数据密钥,把 A 的封装密钥接到 B 的载荷上仍能解开。
        var encryptor = CreateEncryptor(out _);
        var a = Split(encryptor.Encrypt("same value", Purpose));
        var b = Split(encryptor.Encrypt("same value", Purpose));

        Assert.NotEqual(a[2], b[2]);
        Assert.NotEqual(a[3], b[3]);

        var spliced = Join(a[0], a[1], a[2], b[3]);
        Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(spliced, Purpose));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Decrypt_TamperedSegment_Fails(int segment)
    {
        var encryptor = CreateEncryptor(out _);
        var parts = Split(encryptor.Encrypt("secret", Purpose));
        var chars = parts[segment].ToCharArray();
        var middle = chars.Length / 2;
        chars[middle] = chars[middle] == 'A' ? 'B' : 'A';
        parts[segment] = new string(chars);

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(Join(parts), Purpose));

        Assert.False(ex.IsKeyMissing);
    }

    [Fact]
    public void Decrypt_WithDifferentPurpose_Fails()
    {
        var encryptor = CreateEncryptor(out _);
        var cipher = encryptor.Encrypt("secret", Purpose);

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(cipher, OtherPurpose));

        Assert.False(ex.IsKeyMissing);
    }

    [Fact]
    public void WrappedKey_IsBoundToThePurposeIndependentlyOfThePayload()
    {
        // 攻击者把 A 列的封装密钥换到 B 列同形态密文上:载荷用 B 的用途,封装密钥却是按 A 的用途封装的。
        // 即便载荷那一半被替换成对得上的,封装这一半单独也必须失败。
        var encryptor = CreateEncryptor(out _);
        var forPurposeA = Split(encryptor.Encrypt("secret", Purpose));
        var forPurposeB = Split(encryptor.Encrypt("secret", OtherPurpose));

        var swappedWrapped = Join(forPurposeB[0], forPurposeB[1], forPurposeA[2], forPurposeB[3]);

        Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(swappedWrapped, OtherPurpose));
    }

    [Fact]
    public void Decrypt_WithTheKeyIdSwappedToAnotherKey_Fails()
    {
        var encryptor = CreateEncryptor(out _, "m1", "m2");
        var parts = Split(encryptor.Encrypt("secret", Purpose));
        parts[0] = "m2";

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(Join(parts), Purpose));

        Assert.False(ex.IsKeyMissing);
    }

    [Fact]
    public void Decrypt_AfterTheMasterKeyIsRemoved_ReportsKeyMissing()
    {
        var encryptor = CreateEncryptor(out var monitor);
        var cipher = encryptor.Encrypt("to be destroyed", Purpose);

        monitor.Update(o => o.Keys.Clear());

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(cipher, Purpose));
        Assert.True(ex.IsKeyMissing);
    }

    [Fact]
    public void Encrypt_WithoutAnActiveKey_Throws()
    {
        var encryptor = CreateEncryptor(out var monitor);
        monitor.Update(o => o.ActiveKeyId = null);

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Encrypt("value", Purpose));

        Assert.True(ex.IsKeyMissing);
    }

    [Fact]
    public void Rotation_NewValuesUseTheNewKey_OldValuesStillDecrypt()
    {
        var encryptor = CreateEncryptor(out var monitor, "m1", "m2");
        var before = encryptor.Encrypt("written before", Purpose);

        monitor.Update(o => o.ActiveKeyId = "m2");
        var after = encryptor.Encrypt("written after", Purpose);

        Assert.StartsWith("m1:e1:", before, StringComparison.Ordinal);
        Assert.StartsWith("m2:e1:", after, StringComparison.Ordinal);
        Assert.Equal("written before", encryptor.Decrypt(before, Purpose));
        Assert.Equal("written after", encryptor.Decrypt(after, Purpose));
    }

    [Fact]
    public void Rewrap_MovesTheValueToTheActiveKey_WithoutTouchingThePayload()
    {
        var encryptor = CreateEncryptor(out var monitor, "m1", "m2");
        var before = encryptor.Encrypt("long lived token", Purpose);
        monitor.Update(o => o.ActiveKeyId = "m2");

        Assert.True(encryptor.NeedsRewrap(before));
        var rewrapped = encryptor.Rewrap(before, Purpose);

        var oldParts = Split(before);
        var newParts = Split(rewrapped);
        Assert.Equal("m2", newParts[0]);
        Assert.NotEqual(oldParts[2], newParts[2]);
        Assert.Equal(oldParts[3], newParts[3]);
        Assert.False(encryptor.NeedsRewrap(rewrapped));
        Assert.Equal("long lived token", encryptor.Decrypt(rewrapped, Purpose));

        // 换封完成后旧主密钥可以退役,数据照常可读。
        monitor.Update(o => o.Keys.Remove("m1"));
        Assert.Equal("long lived token", encryptor.Decrypt(rewrapped, Purpose));
    }

    [Fact]
    public void Rewrap_OfAValueAlreadyUnderTheActiveKey_ReturnsItUnchanged()
    {
        var encryptor = CreateEncryptor(out _);
        var cipher = encryptor.Encrypt("value", Purpose);

        Assert.False(encryptor.NeedsRewrap(cipher));
        Assert.Equal(cipher, encryptor.Rewrap(cipher, Purpose));
    }

    [Fact]
    public void Rewrap_WithTheWrongPurpose_Fails()
    {
        var encryptor = CreateEncryptor(out var monitor, "m1", "m2");
        var cipher = encryptor.Encrypt("value", Purpose);
        monitor.Update(o => o.ActiveKeyId = "m2");

        Assert.Throws<FieldEncryptionException>(() => encryptor.Rewrap(cipher, OtherPurpose));
    }

    [Fact]
    public void Rewrap_WhenTheOldKeyIsGone_ReportsKeyMissing()
    {
        var encryptor = CreateEncryptor(out var monitor, "m1", "m2");
        var cipher = encryptor.Encrypt("value", Purpose);
        monitor.Update(o =>
        {
            o.ActiveKeyId = "m2";
            o.Keys.Remove("m1");
        });

        var ex = Assert.Throws<FieldEncryptionException>(() => encryptor.Rewrap(cipher, Purpose));

        Assert.True(ex.IsKeyMissing);
    }

    [Fact]
    public void IsEncrypted_RecognisesEnvelopeAndLegacyDirectValues_ButNotPlaintext()
    {
        var encryptor = CreateEncryptor(out var monitor);
        var envelope = encryptor.Encrypt("value", Purpose);
        var direct = new AesGcmFieldEncryptor(monitor).Encrypt("value", Purpose);

        Assert.True(encryptor.IsEncrypted(envelope));
        Assert.True(encryptor.IsEncrypted(direct));
        Assert.False(encryptor.IsEncrypted("plain text value"));
        Assert.False(encryptor.IsEncrypted("m1:e1:only-three"));
        Assert.False(encryptor.IsEncrypted(string.Empty));
    }

    [Fact]
    public void ValuesWrittenInDirectMode_StillDecrypt_AndAreFlaggedForConversion()
    {
        // 从直接模式切到信封模式:存量值不能被当成明文放行,也不能读不出来。
        var encryptor = CreateEncryptor(out var monitor);
        var direct = new AesGcmFieldEncryptor(monitor).Encrypt("legacy value", Purpose);

        Assert.Equal("legacy value", encryptor.Decrypt(direct, Purpose));
        Assert.True(encryptor.NeedsRewrap(direct));

        var converted = encryptor.Rewrap(direct, Purpose);
        Assert.Equal("e1", Split(converted)[1]);
        Assert.Equal("legacy value", encryptor.Decrypt(converted, Purpose));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(4000)]
    public void CiphertextLength_MatchesTheDeclaredOverhead(int plaintextBytes)
    {
        // IsEncrypted() 放宽列长依赖这个常量:它偏小,写入就在数据库层才失败。
        var longestKeyId = new string('k', FieldEncryptionOptions.MaxKeyIdLength);
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            Mode = FieldEncryptionMode.Envelope,
            ActiveKeyId = longestKeyId,
            Keys = new Dictionary<string, string> { [longestKeyId] = NewKey() }
        };
        var encryptor = new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(options)));

        var cipher = encryptor.Encrypt(new string('a', plaintextBytes), Purpose);

        var payloadChars = (plaintextBytes + 28 + 2) / 3 * 4;
        Assert.Equal(EnvelopeFieldEncryptor.OverheadChars + payloadChars, cipher.Length);
    }
}
