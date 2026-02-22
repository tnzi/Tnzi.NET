namespace Tnzi.Imaging.Services;

/// <summary>
/// 验证码服务实现
/// </summary>
public class VerifyCodeService : IVerifyCodeService
{
    private readonly ICache? _cache;
    private readonly ValidateCoder _validateCoder;
    private const string CacheKeyPrefix = "VerifyCode:";

    /// <summary>
    /// 初始化一个<see cref="VerifyCodeService"/>类型的新实例
    /// </summary>
    public VerifyCodeService(ValidateCoder validateCoder, ICache? cache = null)
    {
        _validateCoder = Check.NotNull(validateCoder);
        _cache = cache;
    }

    /// <summary>
    /// 生成验证码
    /// </summary>
    public async Task<VerifyCodeResult> GenerateAsync(int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        return await GenerateAsync(id, codeLength, codeType, 5, cancellationToken);
    }

    /// <summary>
    /// 生成验证码（指定唯一标识）
    /// </summary>
    public async Task<VerifyCodeResult> GenerateAsync(string id, int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, int expireMinutes = 5, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(id);

        // 生成验证码文本
        var code = GenerateCode(codeLength, codeType);

        // 生成验证码图片
        var validateCodeType = codeType switch
        {
            VerifyCodeType.Number => ValidateCodeType.Number,
            VerifyCodeType.Letter => ValidateCodeType.NumberAndLetter, // ValidateCoder没有纯字母类型，使用混合类型
            VerifyCodeType.Mixed => ValidateCodeType.NumberAndLetter,
            _ => ValidateCodeType.Number
        };
        var imageBytes = _validateCoder.CreateImageBytes(code, validateCodeType);

        // 存储验证码到缓存（用于后续验证）
        if (_cache != null)
        {
            var cacheKey = GetCacheKey(id);
            await _cache.SetAsync(cacheKey, code, TimeSpan.FromMinutes(expireMinutes), cancellationToken);
        }

        return new VerifyCodeResult
        {
            Code = code,
            ImageBytes = imageBytes,
            Id = id
        };
    }

    /// <summary>
    /// 验证验证码
    /// </summary>
    public async Task<bool> VerifyAsync(string id, string code, bool ignoreCase = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (string.IsNullOrEmpty(code))
            return false;

        if (_cache == null)
            return false;

        var cacheKey = GetCacheKey(id);
        var storedCode = await _cache.GetAsync<string>(cacheKey, cancellationToken);

        if (string.IsNullOrEmpty(storedCode))
            return false; // 验证码不存在或已过期

        // 先比较验证码，成功后再删除（避免先删后比导致验证失败也消耗验证码）
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var isValid = string.Equals(storedCode, code, comparison);

        if (isValid)
        {
            // 验证成功后删除（一次性使用）
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }

        return isValid;
    }

    /// <summary>
    /// 删除验证码
    /// </summary>
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (_cache == null)
            return;

        var cacheKey = GetCacheKey(id);
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }

    /// <summary>
    /// 生成验证码文本
    /// </summary>
    private string GenerateCode(int length, VerifyCodeType codeType)
    {
        const string numbers = "0123456789";
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // 排除易混淆的字母
        const string mixed = numbers + letters;

        var chars = codeType switch
        {
            VerifyCodeType.Number => numbers,
            VerifyCodeType.Letter => letters,
            VerifyCodeType.Mixed => mixed,
            _ => numbers
        };

        var code = new char[length];
        for (int i = 0; i < length; i++)
        {
            code[i] = chars[Random.Shared.Next(chars.Length)];
        }

        return new string(code);
    }

    /// <summary>
    /// 获取缓存键
    /// </summary>
    private string GetCacheKey(string id)
    {
        return $"{CacheKeyPrefix}{id}";
    }
}
