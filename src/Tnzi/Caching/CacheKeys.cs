namespace Tnzi.Caching;

/// <summary>
/// 统一缓存键管理
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// 为缓存键添加租户前缀
    /// </summary>
    /// <param name="key">原始缓存键</param>
    /// <param name="tenantId">租户 ID</param>
    /// <returns>带租户前缀的缓存键</returns>
    public static string WithTenant(string key, Guid? tenantId)
        => tenantId.HasValue ? $"t:{tenantId.Value:N}:{key}" : key;

    /// <summary>
    /// 身份认证模块缓存键
    /// </summary>
    public static class Identity
    {
        /// <summary>
        /// 用户信息缓存 (user:{id})
        /// </summary>
        public static string User(Guid id) => $"user:{id}";

        /// <summary>
        /// 组织机构详情 (organization:{id})
        /// </summary>
        public static string Organization(Guid id) => $"organization:{id}";

        /// <summary>
        /// 组织机构树 (organization:tree)
        /// </summary>
        public const string OrganizationTree = "organization:tree";

        /// <summary>
        /// 验证码 (captcha:{purpose}:{id})
        /// </summary>
        public static string Captcha(string purpose, string id) => $"captcha:{purpose}:{id}";

        /// <summary>
        /// 登录失败记录 (captcha:failure:{identifier})
        /// </summary>
        public static string LoginFailure(string identifier) => $"captcha:failure:{identifier}";
    }

    /// <summary>
    /// 授权模块缓存键
    /// </summary>
    public static class Authorization
    {
        /// <summary>
        /// 用户功能权限 (UserFunctions:{userId})
        /// </summary>
        public static string UserFunctions(Guid userId) => $"UserFunctions:{userId}";
    }

    /// <summary>
    /// SignalR 模块缓存键
    /// </summary>
    public static class SignalR
    {
        /// <summary>
        /// 用户连接 (SignalR:UserConnections:{userId})
        /// </summary>
        public static string UserConnections(Guid userId) => $"SignalR:UserConnections:{userId}";

        /// <summary>
        /// 消息速率计数 (SignalR:MessageRate:{userId})
        /// </summary>
        public static string MessageRateCount(Guid userId) => $"SignalR:MessageRate:{userId}";

        /// <summary>
        /// 用户封禁状态 (SignalR:Ban:{userId})
        /// </summary>
        public static string UserBan(Guid userId) => $"SignalR:Ban:{userId}";

        /// <summary>
        /// 匿名分区的消息速率计数 (SignalR:Anon:MessageRate:{partitionKey})
        /// </summary>
        /// <remarks>
        /// 匿名连接没有用户 id，按分区键（默认客户端 IP）限流。键空间与按用户的那三个
        /// 分开，避免一个恰好长得像 GUID 的分区键与真实用户撞到同一个计数上。
        /// </remarks>
        public static string AnonymousMessageRateCount(string partitionKey) => $"SignalR:Anon:MessageRate:{partitionKey}";

        /// <summary>
        /// 匿名分区的封禁状态 (SignalR:Anon:Ban:{partitionKey})
        /// </summary>
        public static string AnonymousBan(string partitionKey) => $"SignalR:Anon:Ban:{partitionKey}";

        /// <summary>
        /// 匿名分区的在线连接数 (SignalR:Anon:Connections:{partitionKey})
        /// </summary>
        public static string AnonymousConnectionCount(string partitionKey) => $"SignalR:Anon:Connections:{partitionKey}";
    }
}
