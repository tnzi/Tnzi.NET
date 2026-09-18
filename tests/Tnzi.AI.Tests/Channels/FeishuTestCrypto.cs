using System.Security.Cryptography;
using System.Text;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 按飞书开放平台「事件加密」规范造密文：key = SHA256(EncryptKey)，AES-256-CBC + PKCS7，
/// base64(iv ‖ ciphertext) 装进 <c>{"encrypt":"..."}</c>。配置了 Encrypt Key 之后，
/// 飞书推送的每一个 body（含 url_verification）都是这个形状，签名算在这个密文 body 上。
/// </summary>
internal static class FeishuTestCrypto
{
    public static string Envelope(string plaintextJson, string encryptKey)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptKey));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plaintextJson), aes.IV, PaddingMode.PKCS7);
        var payload = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(payload, 0);
        cipher.CopyTo(payload, aes.IV.Length);

        return JsonSerializer.Serialize(new { encrypt = Convert.ToBase64String(payload) });
    }

    public static Dictionary<string, string> SignedHeaders(string body, string encryptKey, long? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ts + nonce + encryptKey + body));
        return new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = ts,
            ["X-Lark-Request-Nonce"] = nonce,
            ["X-Lark-Signature"] = Convert.ToHexStringLower(hash)
        };
    }
}
