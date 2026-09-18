namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Altcha 挑战（<a href="https://altcha.org/docs/v2/server-integration/">协议</a>）。
/// 字段名与大小写就是控件要求的 wire 形状，不要改。
/// </summary>
/// <remarks>
/// <c>GET /captcha/altcha/challenge</c> 把它<b>裸着</b>回给控件（<see cref="JsonResult"/>，不包 <c>ApiResult</c> 信封）：
/// 控件自己 <c>fetch</c> 这个地址并按「响应体里有没有 <c>challenge</c> 键」校验，信封会被它判成
/// <c>Challenge validation failed</c>。见 <c>DefaultCaptchaController.GetAltchaChallenge</c>。
/// </remarks>
public class AltchaChallengeDto
{
    /// <summary>哈希算法，恒 <c>SHA-256</c>。</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "SHA-256";

    /// <summary><c>hex(sha256(salt + number))</c>，客户端要找出那个 number。</summary>
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    /// <summary>number 的上界，决定客户端的工作量。</summary>
    [JsonPropertyName("maxnumber")]
    public int MaxNumber { get; set; }

    /// <summary>随机盐，带 <c>?expires=</c> 与 <c>&amp;purpose=</c> 查询参数。</summary>
    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    /// <summary><c>hex(hmac_sha256(key, challenge))</c>。</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}
