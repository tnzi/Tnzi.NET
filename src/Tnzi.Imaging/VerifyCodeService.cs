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

        // 映射验证码类型
        var validateCodeType = MapCodeType(codeType);

        // 生成验证码文本（委托给 ValidateCoder 统一管理字符池）
        var code = _validateCoder.GetCode(codeLength, validateCodeType);

        // 生成验证码图片
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

        // 立即删除验证码（一次性使用，无论验证成功与否都消耗）
        // 防止 TOCTOU：并发请求不能对同一验证码重复验证成功
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(storedCode, code, comparison);
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
    /// 映射验证码类型
    /// </summary>
    private static ValidateCodeType MapCodeType(VerifyCodeType codeType)
    {
        return codeType switch
        {
            VerifyCodeType.Number => ValidateCodeType.Number,
            VerifyCodeType.Letter => ValidateCodeType.NumberAndLetter,
            VerifyCodeType.Mixed => ValidateCodeType.NumberAndLetter,
            _ => ValidateCodeType.Number
        };
    }

    /// <summary>
    /// 获取缓存键
    /// </summary>
    private static string GetCacheKey(string id)
    {
        return $"{CacheKeyPrefix}{id}";
    }
}
