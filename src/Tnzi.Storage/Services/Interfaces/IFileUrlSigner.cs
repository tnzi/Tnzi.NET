namespace Tnzi.Storage.Services;

/// <summary>
/// 文件访问令牌的签发与校验。
///
/// 解决的问题只有一个:浏览器发起的资源请求(<c>&lt;img src&gt;</c> / <c>&lt;a download&gt;</c> /
/// <c>&lt;video&gt;</c> / <c>&lt;iframe&gt;</c>)**带不了 Authorization 头**。框架的认证是纯 Bearer,
/// 于是"私密"事实上等同于"浏览器渲染不出来"——连上传者本人也看不见自己的图。
///
/// 做法是短时签名:调用方先用自己的 Bearer 令牌换一个**只对这一个文件、这一小段时间**
/// 有效的令牌,再把它拼进 URL。签发那一刻走完整的读权限判定;消费时只验签名与过期。
///
/// **刻意不是"把 JWT 放进查询参数"**:那会把完整会话凭据泄漏进浏览器历史、referrer
/// 与访问日志,拿到就等于拿到这个人的全部权限。本令牌泄漏的上限是"这一个文件,几分钟"。
/// </summary>
public interface IFileUrlSigner
{
    /// <summary>
    /// 令牌所用的查询参数名。集中在这里,后端校验与前端拼接不会各写各的。
    /// </summary>
    public const string QueryParameterName = "sig";

    /// <summary>
    /// 为一个文件签发访问令牌。**不做权限判定**——调用方(<c>IFileStorageService</c>)
    /// 必须先确认调用者可读。
    /// </summary>
    /// <param name="fileId">文件 ID</param>
    /// <param name="expiresAt">过期时刻(UTC)</param>
    /// <param name="userId">签发给谁(可空)。只用于审计,校验时**不**要求请求方是同一个人
    /// ——消费请求本来就是匿名的,无从比对。</param>
    string Sign(Guid fileId, DateTimeOffset expiresAt, Guid? userId);

    /// <summary>
    /// 校验令牌是否对该文件有效且未过期。
    /// </summary>
    /// <param name="fileId">被访问的文件 ID</param>
    /// <param name="token">查询参数里带来的令牌</param>
    /// <param name="userId">签发时记录的用户(可空),仅供审计</param>
    bool TryValidate(Guid fileId, string? token, out Guid? userId);
}
