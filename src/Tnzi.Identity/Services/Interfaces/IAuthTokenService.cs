

namespace Tnzi.Identity.Services;

/// <summary>
/// 认证令牌服务接口
/// </summary>
public interface IAuthTokenService
{
    /// <summary>
    /// 保存令牌（按 用户+Provider+Name+会话 upsert）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <param name="value">令牌值</param>
    /// <param name="expiresAt">过期时间</param>
    /// <param name="sessionId">所属登录会话ID；<see cref="Guid.Empty"/>（默认）表示不绑定会话（每用户一行，如 2FA 临时令牌）。传入具体会话ID时按会话各存一条（多设备各自独立刷新令牌）</param>
    /// <returns>令牌ID</returns>
    Task<Guid> SaveTokenAsync(Guid userId, string loginProvider, string name, string value, DateTime? expiresAt = null, Guid sessionId = default);

    /// <summary>
    /// 获取令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <returns>令牌值</returns>
    Task<string?> GetTokenAsync(Guid userId, string loginProvider, string name);

    /// <summary>
    /// 删除令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    Task RemoveTokenAsync(Guid userId, string loginProvider, string name);

    /// <summary>
    /// 删除用户的所有令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者（可选）</param>
    Task RemoveAllTokensAsync(Guid userId, string? loginProvider = null);

    /// <summary>
    /// 标记令牌为已使用（带并发控制，已被使用的令牌返回 false）
    /// </summary>
    /// <param name="tokenId">令牌ID</param>
    /// <returns>标记成功返回 true；令牌不存在或已被使用返回 false</returns>
    Task<bool> MarkTokenAsUsedAsync(Guid tokenId);

    /// <summary>
    /// 清理过期的令牌
    /// </summary>
    /// <returns>清理的令牌数量</returns>
    Task<int> CleanExpiredTokensAsync();

    /// <summary>
    /// 获取用户的所有令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者（可选）</param>
    /// <returns>令牌列表</returns>
    Task<IEnumerable<AuthToken>> GetUserTokensAsync(Guid userId, string? loginProvider = null);

    /// <summary>
    /// 通过令牌值查找令牌（用于临时Token查找用户）
    /// </summary>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <param name="value">令牌值</param>
    /// <returns>令牌实体，如果不存在则返回null</returns>
    Task<AuthToken?> FindTokenByValueAsync(string loginProvider, string name, string value);

    /// <summary>
    /// 按<b>上一代</b>令牌值查找令牌（刷新令牌重放检测）。
    /// </summary>
    /// <remarks>
    /// 用途只有一个：当前值查不到时，问一句「这枚令牌是不是刚被轮换掉的那一代」。
    /// 是 → 要么是并发刷新（宽限窗内），要么是重放；不是 → 就是一枚无效令牌。
    /// 刻意<b>不过滤过期</b>：一枚过期的旧令牌被拿来重放，同样是「有人在用不属于他的东西」，
    /// 只是它所属的会话多半也已经不在了，撤销会落空 —— 落空不影响事件被发出去。
    /// </remarks>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <param name="value">待检查的令牌<b>明文</b>（内部按 <see cref="OneTimeToken.Hash"/> 比对）</param>
    /// <returns>轮换后的当前令牌实体；没有任何一代匹配则返回 null</returns>
    Task<AuthToken?> FindTokenByPreviousValueAsync(string loginProvider, string name, string value);

    /// <summary>
    /// 还原一枚令牌的明文值；解不开（key ring 轮换 / 丢失）时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <see cref="AuthToken.Value"/> 存的是密文，实体本身给不出明文 —— 拿到实体的调用方
    /// 必须经这里还原。目前只有一个调用点：<b>刷新令牌轮换的宽限窗</b>要把当前这一代的
    /// 明文交给并发刷新的那一方（<c>AuthService.HandleRotatedRefreshTokenAsync</c>）。
    /// </para>
    /// <para>
    /// ★ 命名刻意用 <c>Reveal</c> 而不是 <c>GetValue</c>：调用它意味着「把一枚可用凭据
    /// 取到内存里」，那应当是一个读起来就需要停一下的动作。
    /// </para>
    /// </remarks>
    /// <param name="token">令牌实体。</param>
    string? RevealTokenValue(AuthToken token);

    /// <summary>
    /// 轮换刷新令牌：把当前值换成新值，并把当前值的哈希留在 <see cref="AuthToken.PreviousValueHash"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>整个操作是一次条件更新</b>（<c>WHERE Id = @id AND Value = @expectedCurrentValue</c>），
    /// 而不是「读出来、改、存回去」。旧实现的「先 <see cref="MarkTokenAsUsedAsync"/>
    /// 再 <c>SaveTokenAsync</c>」是两次写：中间那一瞬旧值已经消失、新值还没落库，
    /// 并发的第二个刷新请求会看到一行没有任何令牌值的记录。条件更新把抢占与写入合成一步，
    /// 输的那一方拿到 <c>false</c>，可以干净地走并发分支。
    /// </para>
    /// <para>
    /// 顺带把 <see cref="AuthToken.IsUsed"/> 复位：这一行代表「这个会话当前有效的刷新令牌」，
    /// 它是长期存在的，不是一次性凭据。
    /// </para>
    /// </remarks>
    /// <param name="tokenId">令牌ID</param>
    /// <param name="expectedCurrentValue">调用方读到的当前值 —— 条件更新的抢占条件</param>
    /// <param name="newValue">新令牌值</param>
    /// <param name="expiresAt">新的过期时间</param>
    /// <returns>抢占成功返回 true；已被别的请求先一步轮换返回 false</returns>
    Task<bool> RotateRefreshTokenAsync(Guid tokenId, string expectedCurrentValue, string newValue, DateTime? expiresAt);

    /// <summary>
    /// 删除绑定在这些会话上的全部令牌。
    /// </summary>
    /// <remarks>
    /// 撤销会话时必须连带做的事。会话行上的 <c>IsRevoked</c> 只挡住了「用 access token 访问」
    /// 与「刷新时的会话校验」两条路；令牌行留着，就等于把撤销的有效性押在
    /// <c>EnforceSessionValidation</c> 这个逃生开关一直开着上。
    /// </remarks>
    /// <param name="sessionIds">会话ID集合；空集合直接返回 0</param>
    /// <returns>删除的令牌数</returns>
    Task<int> RemoveSessionTokensAsync(IReadOnlyCollection<Guid> sessionIds);

    /// <summary>
    /// 删除某用户<b>全部会话绑定</b>的令牌（可排除一个会话）。
    /// </summary>
    /// <remarks>
    /// ★ 按用户删而不是先枚举会话再按 id 删，是为了避开一个竞态：枚举与撤销之间新建的会话，
    /// 它的刷新令牌不在枚举结果里，于是「全部撤销」之后仍有一枚活着的刷新令牌。
    /// 按用户一次删干净，就不存在这个缝。
    /// <para>
    /// 不触碰未绑定会话的令牌（<see cref="AuthToken.SessionId"/> 为 <see cref="Guid.Empty"/>：
    /// 2FA 临时令牌、passkey 注册令牌、设密令牌）—— 那些走各自的一次性消费路径，
    /// 且都会在签发前重新过一遍登录守卫，不需要在这里连坐。
    /// </para>
    /// </remarks>
    /// <param name="userId">用户ID</param>
    /// <param name="excludeSessionId">保留的会话</param>
    /// <returns>删除的令牌数</returns>
    Task<int> RemoveUserSessionTokensAsync(Guid userId, Guid? excludeSessionId = null);
}

