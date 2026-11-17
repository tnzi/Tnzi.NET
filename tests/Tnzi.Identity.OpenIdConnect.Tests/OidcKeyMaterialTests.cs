using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Tnzi.Exceptions;
using Tnzi.Identity.OpenIdConnect.Options;
using Tnzi.Identity.OpenIdConnect.Services;

namespace Tnzi.Identity.OpenIdConnect.Tests;

/// <summary>密钥环的读取规则，以及「生产环境没配密钥就起不来」。</summary>
public sealed class OidcKeyMaterialTests
{
    private static string RsaPem(int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static string AesKey(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    [Fact]
    public void TheActiveKeysComeFirst_TheOthersStayForValidationAndDecryption()
    {
        var errors = new List<string>();
        var keys = OidcKeyMaterial.Load(new OpenIdConnectOptions
        {
            SigningKeys = { ["old"] = RsaPem(), ["new"] = RsaPem() },
            ActiveSigningKeyId = "new",
            EncryptionKeys = { ["e-old"] = AesKey(), ["e-new"] = AesKey() },
            ActiveEncryptionKeyId = "e-new",
        }, errors);

        errors.ShouldBeEmpty();
        keys.SigningCredentials.Select(c => c.Key.KeyId).ShouldBe(["new", "old"]);
        keys.EncryptingCredentials.Select(c => c.Key.KeyId).ShouldBe(["e-new", "e-old"]);
    }

    [Fact]
    public void EcKeys_AreAccepted_WithTheMatchingAlgorithm()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var errors = new List<string>();

        var keys = OidcKeyMaterial.Load(new OpenIdConnectOptions
        {
            SigningKeys = { ["ec"] = ec.ExportPkcs8PrivateKeyPem() },
            EncryptionKeys = { ["e"] = AesKey() },
        }, errors);

        errors.ShouldBeEmpty();
        keys.SigningCredentials.Single().Algorithm.ShouldBe(SecurityAlgorithms.EcdsaSha256);
    }

    [Fact]
    public void BadMaterial_IsReportedPerKey()
    {
        using var rsa = RSA.Create(2048);
        var errors = new List<string>();

        OidcKeyMaterial.Load(new OpenIdConnectOptions
        {
            SigningKeys =
            {
                ["short"] = RsaPem(1024),
                ["public-only"] = rsa.ExportSubjectPublicKeyInfoPem(),
                ["garbage"] = "not a key",
            },
            ActiveSigningKeyId = "short",
            EncryptionKeys = { ["not-base64"] = "%%%", ["too-short"] = AesKey(16), ["fine"] = AesKey() },
            ActiveEncryptionKeyId = "missing",
        }, errors);

        errors.ShouldContain(e => e.Contains("'short'") && e.Contains("2048"));
        errors.ShouldContain(e => e.Contains("'public-only'"));
        errors.ShouldContain(e => e.Contains("'garbage'"));
        errors.ShouldContain(e => e.Contains("'not-base64'"));
        errors.ShouldContain(e => e.Contains("'too-short'") && e.Contains("32 bytes"));
        errors.ShouldContain(e => e.Contains("ActiveEncryptionKeyId 'missing'"));
    }

    [Fact]
    public void TwoKeysWithoutAnActiveId_IsAnError()
    {
        var errors = new List<string>();

        OidcKeyMaterial.Load(new OpenIdConnectOptions { SigningKeys = { ["a"] = RsaPem(), ["b"] = RsaPem() } }, errors);

        errors.ShouldContain(e => e.Contains("ActiveSigningKeyId is required"));
    }

    [Fact]
    public void AKeyFile_IsMergedWithConfiguration_AndDuplicateIdsAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "tnzi-oidc-keys-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                activeSigningKeyId = "file-rsa",
                signingKeys = new Dictionary<string, string> { ["file-rsa"] = RsaPem() },
                encryptionKeys = new Dictionary<string, string> { ["dup"] = AesKey() },
            }));

            var errors = new List<string>();
            var keys = OidcKeyMaterial.Load(new OpenIdConnectOptions { KeyFile = path, EncryptionKeys = { ["dup"] = AesKey() } }, errors);

            errors.ShouldContain(e => e.Contains("'dup'") && e.Contains("both in configuration and in the key file"));
            keys.SigningCredentials.Single().Key.KeyId.ShouldBe("file-rsa");

            var missing = new List<string>();
            OidcKeyMaterial.Load(new OpenIdConnectOptions { KeyFile = path + ".missing" }, missing);
            missing.ShouldContain(e => e.Contains("does not exist"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OutsideDevelopment_NoKeys_FailsLoudly()
    {
        var setup = new OidcServerOptionsSetup(
            Microsoft.Extensions.Options.Options.Create(new OpenIdConnectOptions()),
            new FakeEnvironment(Environments.Production),
            NullLogger<OidcServerOptionsSetup>.Instance);

        Should.Throw<ConfigurationException>(() => setup.Configure(new OpenIddictServerOptions()));
    }

    [Fact]
    public void InDevelopment_NoKeys_FallsBackToEphemeralKeys()
    {
        var setup = new OidcServerOptionsSetup(
            Microsoft.Extensions.Options.Options.Create(new OpenIdConnectOptions()),
            new FakeEnvironment(Environments.Development),
            NullLogger<OidcServerOptionsSetup>.Instance);
        var options = new OpenIddictServerOptions();

        setup.Configure(options);

        options.SigningCredentials.ShouldHaveSingleItem();
        options.EncryptionCredentials.ShouldHaveSingleItem();
    }

    [Fact]
    public void TheValidator_SurfacesKeyErrorsAndBadUrls_AtStartup()
    {
        var result = new OpenIdConnectOptionsValidator().Validate(null, new OpenIdConnectOptions
        {
            Issuer = "not a url",
            LoginUrl = "//evil.example",
            SigningKeys = { ["x"] = "garbage" },
            SessionCookie = { Path = "connect" },
        });

        result.Failed.ShouldBeTrue();
        var failures = string.Join(" | ", result.Failures!);
        failures.ShouldContain("Issuer");
        failures.ShouldContain("LoginUrl");
        failures.ShouldContain("SessionCookie:Path");
        failures.ShouldContain("'x'");
    }

    [Fact]
    public void TheValidator_AcceptsTheDefaults()
    {
        new OpenIdConnectOptionsValidator().Validate(null, new OpenIdConnectOptions()).Succeeded.ShouldBeTrue();
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
