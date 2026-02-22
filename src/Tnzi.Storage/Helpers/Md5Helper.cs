namespace Tnzi.Storage.Helpers;

/// <summary>
/// MD5 计算辅助类
/// </summary>
internal static class Md5Helper
{
    /// <summary>
    /// 异步计算流的 MD5 哈希
    /// </summary>
    internal static async Task<string> CalculateAsync(Stream stream)
    {
        using var md5 = MD5.Create();
        var hashBytes = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
