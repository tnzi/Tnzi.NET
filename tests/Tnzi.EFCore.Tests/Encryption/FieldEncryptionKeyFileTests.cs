using System.Text.Json;
using Microsoft.Extensions.Options;
using Tnzi.Security;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 从密钥文件读主密钥:格式、启动期校验、与配置密钥环并用,以及文件变化后的重新读取。
/// </summary>
public sealed class FieldEncryptionKeyFileTests : IDisposable
{
    private const string Purpose = "Demo.Connection.AccessToken";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tnzi-keyfile-" + Guid.NewGuid().ToString("N"));

    public FieldEncryptionKeyFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string NewKey() => AesGcmHelper.GenerateKeyBase64();

    private string WriteKeyFile(string? activeKeyId, params (string Id, string Material)[] keys)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        WriteKeyFile(path, activeKeyId, keys);
        return path;
    }

    private static void WriteKeyFile(string path, string? activeKeyId, params (string Id, string Material)[] keys)
    {
        var document = new Dictionary<string, object?>
        {
            ["keys"] = keys.ToDictionary(k => k.Id, k => k.Material)
        };
        if (activeKeyId is not null)
        {
            document["activeKeyId"] = activeKeyId;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(document));
    }

    private static FieldEncryptionOptions Options(string keyFile, FieldEncryptionMode mode = FieldEncryptionMode.Envelope) => new()
    {
        Enabled = true,
        Mode = mode,
        KeyFile = keyFile
    };

    private static List<string> ValidationFailures(FieldEncryptionOptions options)
    {
        var result = new FieldEncryptionOptionsValidator().Validate(null, options);
        return result.Failed ? [.. result.Failures] : [];
    }

    [Fact]
    public void KeyFile_IsEnoughToEncryptAndDecrypt()
    {
        var path = WriteKeyFile("2026-10", ("2026-10", NewKey()), ("2026-01", NewKey()));
        var encryptor = new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(Options(path))));

        var cipher = encryptor.Encrypt("imap-app-password", Purpose);

        Assert.StartsWith("2026-10:e1:", cipher, StringComparison.Ordinal);
        Assert.Equal("imap-app-password", encryptor.Decrypt(cipher, Purpose));
    }

    [Fact]
    public void KeyFile_AlsoDrivesDirectMode()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));
        var encryptor = new AesGcmFieldEncryptor(new MutableFieldEncryptionOptionsMonitor(Options(path, FieldEncryptionMode.Direct)));

        Assert.Equal("value", encryptor.Decrypt(encryptor.Encrypt("value", Purpose), Purpose));
    }

    [Fact]
    public void ConfiguredActiveKeyId_OverridesTheOneInTheFile()
    {
        var path = WriteKeyFile("old", ("old", NewKey()), ("new", NewKey()));
        var options = Options(path);
        options.ActiveKeyId = "new";
        var encryptor = new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(options)));

        Assert.StartsWith("new:e1:", encryptor.Encrypt("v", Purpose), StringComparison.Ordinal);
    }

    [Fact]
    public void FileKeysAndConfiguredKeys_FormOneRing()
    {
        var path = WriteKeyFile("fromfile", ("fromfile", NewKey()));
        var options = Options(path);
        options.Keys["fromconfig"] = NewKey();
        var monitor = new MutableFieldEncryptionOptionsMonitor(options);
        var ring = new FieldEncryptionKeyRing(monitor);

        Assert.True(ring.Contains("fromfile"));
        Assert.True(ring.Contains("fromconfig"));
        Assert.False(ring.Contains("nowhere"));
        Assert.Empty(ValidationFailures(options));
    }

    [Fact]
    public void Validator_RejectsAMissingKeyFile_NamingThePath()
    {
        var missing = Path.Combine(_directory, "absent.json");

        var failures = ValidationFailures(Options(missing));

        Assert.Contains(failures, f => f.Contains(missing, StringComparison.Ordinal) && f.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"keys\": {} }")]
    [InlineData("{ \"activeKeyId\": \"k\", \"key\": { \"k\": \"AAAA\" } }")]
    [InlineData("[]")]
    public void Validator_RejectsAMalformedKeyFile(string content)
    {
        var path = Path.Combine(_directory, "bad.json");
        File.WriteAllText(path, content);

        var failures = ValidationFailures(Options(path));

        Assert.NotEmpty(failures);
        Assert.All(failures, f => Assert.Contains(path, f, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsAKeyOfTheWrongLength_WithoutEchoingTheMaterial()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        var path = WriteKeyFile("k1", ("k1", shortKey));

        var failures = ValidationFailures(Options(path));

        Assert.NotEmpty(failures);
        Assert.DoesNotContain(failures, f => f.Contains(shortKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsAnActiveKeyThatIsNotInTheFile()
    {
        var path = WriteKeyFile("ghost", ("k1", NewKey()));

        Assert.NotEmpty(ValidationFailures(Options(path)));
    }

    [Fact]
    public void Validator_RejectsAKeyIdDefinedInBothPlaces()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));
        var options = Options(path);
        options.Keys["k1"] = NewKey();

        Assert.NotEmpty(ValidationFailures(options));
    }

    [Fact]
    public void Validator_AcceptsAKeyFileWithoutConfiguredKeys()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));

        Assert.Empty(ValidationFailures(Options(path)));
    }

    [Fact]
    public void Ring_WhenTheFileIsMissingAtFirstUse_Throws()
    {
        var ring = new FieldEncryptionKeyRing(
            new MutableFieldEncryptionOptionsMonitor(Options(Path.Combine(_directory, "absent.json"))));

        var ex = Assert.Throws<FieldEncryptionException>(() => ring.Contains("k1"));

        Assert.Contains("key file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ring_PicksUpAReplacedKeyFile_WithoutARestart()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));
        var encryptor = new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(Options(path))));
        var old = encryptor.Encrypt("before rotation", Purpose);
        Assert.StartsWith("k1:", old, StringComparison.Ordinal);

        // 轮换:新文件里 k2 成为活动密钥,k1 保留。
        var original = File.ReadAllText(path);
        var k1 = JsonDocument.Parse(original).RootElement.GetProperty("keys").GetProperty("k1").GetString()!;
        WriteKeyFile(path, "k2", ("k1", k1), ("k2", NewKey()));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Thread.Sleep(FieldEncryptionKeyRing.FileCheckIntervalMs + 200);

        var fresh = encryptor.Encrypt("after rotation", Purpose);

        Assert.StartsWith("k2:", fresh, StringComparison.Ordinal);
        Assert.Equal("before rotation", encryptor.Decrypt(old, Purpose));
        Assert.Equal("after rotation", encryptor.Decrypt(fresh, Purpose));
    }

    [Fact]
    public void Ring_KeepsThePreviousKeysWhenTheReplacementIsUnreadable()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));
        var encryptor = new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(Options(path))));
        var cipher = encryptor.Encrypt("value", Purpose);

        File.WriteAllText(path, "{ half written");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Thread.Sleep(FieldEncryptionKeyRing.FileCheckIntervalMs + 200);

        Assert.Equal("value", encryptor.Decrypt(cipher, Purpose));
    }

    [Fact]
    public void Container_BuildsTheEnvelopeEncryptor_FromTheOptions()
    {
        var path = WriteKeyFile("k1", ("k1", NewKey()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptionsMonitor<FieldEncryptionOptions>>(new MutableFieldEncryptionOptionsMonitor(Options(path)));
        services.AddSingleton<FieldEncryptionKeyRing>();
        services.AddSingleton<IFieldEncryptor>(sp => new EnvelopeFieldEncryptor(sp.GetRequiredService<FieldEncryptionKeyRing>()));
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var encryptor = provider.GetRequiredService<IFieldEncryptor>();

        Assert.IsType<EnvelopeFieldEncryptor>(encryptor);
        Assert.Equal("v", encryptor.Decrypt(encryptor.Encrypt("v", Purpose), Purpose));
    }
}
