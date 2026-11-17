using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 从配置与密钥文件读出的签名 / 加密密钥环。当前（Active）密钥排在第一位：OpenIddict 用第一把签名 / 加密，
/// 其余的签名密钥仍出现在 JWKS 里供验签，其余的加密密钥仍用于解密。
/// </summary>
public sealed class OidcKeyMaterial
{
    /// <summary>对称加密密钥的字节数（256 位）。</summary>
    public const int EncryptionKeyBytes = 32;

    /// <summary>RSA 签名密钥的最小位数。</summary>
    public const int MinimumRsaKeySize = 2048;

    private OidcKeyMaterial(IReadOnlyList<SigningCredentials> signing, IReadOnlyList<EncryptingCredentials> encryption)
    {
        SigningCredentials = signing;
        EncryptingCredentials = encryption;
    }

    /// <summary>签名凭据，当前密钥在前。</summary>
    public IReadOnlyList<SigningCredentials> SigningCredentials { get; }

    /// <summary>加密凭据，当前密钥在前。</summary>
    public IReadOnlyList<EncryptingCredentials> EncryptingCredentials { get; }

    /// <summary>两个环都至少有一把密钥。</summary>
    public bool IsComplete => SigningCredentials.Count > 0 && EncryptingCredentials.Count > 0;

    /// <summary>一把都没配（开发环境可以退回临时密钥的唯一情形）。</summary>
    public bool IsEmpty => SigningCredentials.Count == 0 && EncryptingCredentials.Count == 0;

    /// <summary>
    /// 读密钥环。任何一处格式错误都进 <paramref name="errors"/>，此时返回的结果不可用。
    /// </summary>
    public static OidcKeyMaterial Load(OpenIdConnectOptions options, List<string> errors)
    {
        Check.NotNull(options);
        Check.NotNull(errors);

        var signingPem = new Dictionary<string, string>(options.SigningKeys, StringComparer.Ordinal);
        var encryptionBase64 = new Dictionary<string, string>(options.EncryptionKeys, StringComparer.Ordinal);
        var activeSigning = options.ActiveSigningKeyId;
        var activeEncryption = options.ActiveEncryptionKeyId;

        if (!string.IsNullOrWhiteSpace(options.KeyFile))
        {
            var file = ReadKeyFile(options.KeyFile, errors);
            if (file != null)
            {
                Merge(file.SigningKeys, signingPem, "signingKeys", errors);
                Merge(file.EncryptionKeys, encryptionBase64, "encryptionKeys", errors);
                activeSigning = string.IsNullOrWhiteSpace(activeSigning) ? file.ActiveSigningKeyId : activeSigning;
                activeEncryption = string.IsNullOrWhiteSpace(activeEncryption) ? file.ActiveEncryptionKeyId : activeEncryption;
            }
        }

        var signing = new List<(string Id, SigningCredentials Credentials)>();
        foreach (var (id, pem) in signingPem)
        {
            var credentials = CreateSigningCredentials(id, pem, errors);
            if (credentials != null)
            {
                signing.Add((id, credentials));
            }
        }

        var encryption = new List<(string Id, EncryptingCredentials Credentials)>();
        foreach (var (id, material) in encryptionBase64)
        {
            var credentials = CreateEncryptingCredentials(id, material, errors);
            if (credentials != null)
            {
                encryption.Add((id, credentials));
            }
        }

        return new OidcKeyMaterial(
            OrderActiveFirst(signing, activeSigning, "ActiveSigningKeyId", errors),
            OrderActiveFirst(encryption, activeEncryption, "ActiveEncryptionKeyId", errors));
    }

    /// <summary>
    /// 开发环境的临时密钥：进程内生成，重启即失效。只在一把密钥都没配时由模块调用。
    /// </summary>
    public static OidcKeyMaterial CreateEphemeral()
    {
        var rsa = new RsaSecurityKey(RSA.Create(MinimumRsaKeySize)) { KeyId = "ephemeral-" + Guid.NewGuid().ToString("N")[..8] };
        var aes = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(EncryptionKeyBytes)) { KeyId = rsa.KeyId };
        return new OidcKeyMaterial(
            [new SigningCredentials(rsa, SecurityAlgorithms.RsaSha256)],
            [new EncryptingCredentials(aes, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512)]);
    }

    private static List<T> OrderActiveFirst<T>(List<(string Id, T Credentials)> ring, string? activeId, string setting, List<string> errors)
    {
        if (ring.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(activeId))
        {
            if (ring.Count > 1)
            {
                errors.Add($"{setting} is required when the key ring holds more than one key.");
            }

            return ring.Select(k => k.Credentials).ToList();
        }

        var active = ring.FindIndex(k => k.Id == activeId);
        if (active < 0)
        {
            errors.Add($"{setting} '{activeId}' is not present in the configured key ring.");
            return ring.Select(k => k.Credentials).ToList();
        }

        return ring.Skip(active).Take(1).Concat(ring.Where((_, i) => i != active)).Select(k => k.Credentials).ToList();
    }

    private static SigningCredentials? CreateSigningCredentials(string id, string pem, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            errors.Add($"signing key '{id}' is empty.");
            return null;
        }

        // PKCS#8 的「PRIVATE KEY」标签同时装得下 RSA 与 EC，只能按顺序试：导入失败的那一种不算错。
        var rsa = TryImport(RSA.Create(), pem);
        if (rsa != null)
        {
            if (rsa.KeySize < MinimumRsaKeySize)
            {
                errors.Add($"signing key '{id}' is a {rsa.KeySize}-bit RSA key; at least {MinimumRsaKeySize} bits are required.");
                rsa.Dispose();
                return null;
            }

            if (!HasPrivatePart(() => rsa.ExportParameters(includePrivateParameters: true).D))
            {
                errors.Add($"signing key '{id}' has no private part (a public key cannot sign).");
                rsa.Dispose();
                return null;
            }

            return new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = id }, SecurityAlgorithms.RsaSha256);
        }

        var ec = TryImport(ECDsa.Create(), pem);
        if (ec != null)
        {
            if (!HasPrivatePart(() => ec.ExportParameters(includePrivateParameters: true).D))
            {
                errors.Add($"signing key '{id}' has no private part (a public key cannot sign).");
                ec.Dispose();
                return null;
            }

            var algorithm = ec.KeySize switch
            {
                256 => SecurityAlgorithms.EcdsaSha256,
                384 => SecurityAlgorithms.EcdsaSha384,
                521 => SecurityAlgorithms.EcdsaSha512,
                _ => null,
            };
            if (algorithm == null)
            {
                errors.Add($"signing key '{id}' uses an unsupported curve ({ec.KeySize} bits); use P-256, P-384 or P-521.");
                ec.Dispose();
                return null;
            }

            return new SigningCredentials(new ECDsaSecurityKey(ec) { KeyId = id }, algorithm);
        }

        errors.Add($"signing key '{id}' is not a PEM-encoded RSA or EC private key.");
        return null;
    }

    private static T? TryImport<T>(T algorithm, string pem)
        where T : AsymmetricAlgorithm
    {
        try
        {
            algorithm.ImportFromPem(pem);
            return algorithm;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            algorithm.Dispose();
            return null;
        }
    }

    private static bool HasPrivatePart(Func<byte[]?> exportPrivate)
    {
        try
        {
            return exportPrivate() is { Length: > 0 };
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static EncryptingCredentials? CreateEncryptingCredentials(string id, string material, List<string> errors)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(material ?? string.Empty);
        }
        catch (FormatException)
        {
            errors.Add($"encryption key '{id}' is not valid Base64.");
            return null;
        }

        if (bytes.Length != EncryptionKeyBytes)
        {
            errors.Add($"encryption key '{id}' must be {EncryptionKeyBytes} bytes (256 bits); it is {bytes.Length} bytes.");
            return null;
        }

        return new EncryptingCredentials(
            new SymmetricSecurityKey(bytes) { KeyId = id },
            SecurityAlgorithms.Aes256KW,
            SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    private static void Merge(Dictionary<string, string>? source, Dictionary<string, string> target, string name, List<string> errors)
    {
        if (source == null)
        {
            return;
        }

        foreach (var (id, value) in source)
        {
            if (!target.TryAdd(id, value))
            {
                errors.Add($"key id '{id}' is defined both in configuration and in the key file ({name}).");
            }
        }
    }

    private static KeyFileModel? ReadKeyFile(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"KeyFile '{path}' does not exist.");
            return null;
        }

        try
        {
            var model = JsonSerializer.Deserialize<KeyFileModel>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (model == null)
            {
                errors.Add($"KeyFile '{path}' is empty.");
            }

            return model;
        }
        catch (JsonException ex)
        {
            errors.Add($"KeyFile '{path}' is not valid JSON: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            errors.Add($"KeyFile '{path}' could not be read: {ex.Message}");
            return null;
        }
    }

    private sealed class KeyFileModel
    {
        public string? ActiveSigningKeyId { get; set; }

        public Dictionary<string, string>? SigningKeys { get; set; }

        public string? ActiveEncryptionKeyId { get; set; }

        public Dictionary<string, string>? EncryptionKeys { get; set; }
    }
}
