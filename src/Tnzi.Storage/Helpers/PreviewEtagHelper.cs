namespace Tnzi.Storage.Helpers;

/// <summary>
/// 预览 / 缩略图响应的 <c>ETag</c>：由记录 id 与 MD5 派生的不透明强校验器。
/// </summary>
/// <remarks>
/// 不直接发 <see cref="FileRecord.Md5Hash"/>：<c>FileRecordDto</c> 的契约是 MD5 不对普通读者外露
/// （只有管理端的完整性校验端点单独给），而预览路由匿名可达（公开文件 / <c>?sig=</c>）；同一份字节的两条记录
/// 若发同一个 ETag，读者还能据此看出它们经 MD5 去重指向同一对象。派生值随 MD5 改变（建版本 / 还原版本改写 MD5，
/// 旧副本在第一次重验证时就被换掉），仍是强校验器；同一条记录同一份字节恒得同一个值。
/// 消费方整体替换默认控制器时请沿用本方法，让 <c>If-None-Match</c> 的比对口径一致。
/// </remarks>
public static class PreviewEtagHelper
{
    /// <summary>
    /// 算出带引号的强 ETag；记录没有 MD5 时返回 null（不发 ETag）。
    /// </summary>
    public static string? Compute(FileRecord record)
    {
        Check.NotNull(record);
        if (string.IsNullOrEmpty(record.Md5Hash))
            return null;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{record.Id:N}:{record.Md5Hash}"));
        return $"\"{Convert.ToHexStringLower(digest.AsSpan(0, 16))}\"";
    }
}
