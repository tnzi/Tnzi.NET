namespace Tnzi.Audit.Options;

/// <summary>
/// 审计模块配置选项
/// </summary>
[ConfigSection("Audit")]
[RuntimeSettingGroup(Key = "audit-retention", Module = "Audit", DisplayName = "Retention",
    I18nKey = "admin.modules.system.settings.groups.auditRetention",
    Icon = "mdi:archive-clock-outline", Order = 600)]
public class AuditOptions
{
    /// <summary>是否启用操作审计</summary>
    [RuntimeSetting(Label = "Operation Audit", I18n = "admin.modules.system.settings.fields.auditEnableOperation",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Master switch for API operation audit logging (request/response metadata)")]
    public bool EnableOperationAudit { get; set; } = true;

    /// <summary>
    /// 是否启用实体变更审计。
    /// 由 EntityAuditSaveChangesInterceptor 经 IOptionsMonitor 热读，关闭时零采集开销；
    /// 实体条目挂在请求级 AuditOperation 上，持久化同时依赖 EnableOperationAudit 开启。
    /// </summary>
    [RuntimeSetting(Label = "Entity Audit", I18n = "admin.modules.system.settings.fields.auditEnableEntity",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Capture entity-level changes (added/modified/deleted entities with property old/new values) for each audited operation; sensitive fields are redacted")]
    public bool EnableEntityAudit { get; set; } = true;

    /// <summary>
    /// 操作审计（<c>Audit_Operation</c> 及其级联的实体 / 属性条目）的保留天数。
    /// </summary>
    /// <remarks>
    /// 这是日志表的容量管理，不是合规销毁（那是 <c>Audit:DataDestruction</c>，管的是业务数据且要出证明）。
    /// 它是手动清理端点 <c>DELETE admin/audit-operations/expired</c> 的缺省参数；
    /// 只有 <see cref="AutoPurgeEnabled"/> 打开时，<see cref="AuditRetentionBackgroundService"/> 才会按它自动删。
    /// 配置中心里它挂在「Retention」组下，此前没有说明，操作者会自然读成「超过 N 天自动删」——
    /// 而那件事默认并不发生，所以 Description 必须把两种语义都写出来。
    /// </remarks>
    [RuntimeSetting(Label = "Retention Days", I18n = "admin.modules.system.settings.fields.retentionDays",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Audit operations older than this many days are removed by the manual purge endpoint (DELETE admin/audit-operations/expired) and, when Automatic Purge is on, by the scheduled purge. Nothing is removed on its own while Automatic Purge is off")]
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// 是否按 <see cref="RetentionDays"/> 定时自动清理过期的操作审计。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意默认关闭：升级后凭空开始删审计历史不是任何部署方签过字的事。开着的部署由
    /// <see cref="AuditRetentionBackgroundService"/> 每 <see cref="AutoPurgeIntervalHours"/> 小时删一次，
    /// 多实例经 <c>IDistributedLock</c> 互斥（无实现时各实例各删各的，结果幂等只是多几条日志）。
    /// </para>
    /// <para>
    /// 热读：后台服务每轮都看这个值，配置中心里关掉即从下一轮起停止删除，不必重启。
    /// </para>
    /// </remarks>
    [RuntimeSetting(Label = "Automatic Purge", I18n = "admin.modules.system.settings.fields.auditAutoPurgeEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Remove audit operations older than Retention Days on a schedule (every Audit:AutoPurgeIntervalHours hours, default 24). Off by default so an upgrade never starts deleting audit history on its own; the manual purge endpoint works either way")]
    public bool AutoPurgeEnabled { get; set; }

    /// <summary>
    /// 两次自动清理之间的间隔小时数。默认 <c>24</c>，取值 1 到 <see cref="MaxAutoPurgeIntervalHours"/>。
    /// </summary>
    /// <remarks>
    /// 保留期以天为量级，没有必要扫得更勤；这个值只影响「到期后多久被删掉」的延迟上界。
    /// 不是热设置（改了要重启）：它决定的是后台循环的睡眠时长，而不是删不删。
    /// </remarks>
    public int AutoPurgeIntervalHours { get; set; } = 24;

    /// <summary>
    /// <see cref="AutoPurgeIntervalHours"/> 的上限：30 天。
    /// </summary>
    /// <remarks>
    /// 不是品味：<c>Task.Delay</c> 能等的最长时间约 49 天，越过它后台循环会在第一轮之后抛
    /// <c>ArgumentOutOfRangeException</c>，而未处理的后台服务异常默认把整个宿主停掉 —— 一个写大了的配置项
    /// 变成启动一分钟后的宕机。校验器在启动时就挡下它；30 天本身也远超任何合理的扫描间隔。
    /// </remarks>
    public const int MaxAutoPurgeIntervalHours = 24 * 30;

    /// <summary>批量处理大小</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 单次 CSV / JSON 导出允许的最大行数。过滤结果超过它时导出<b>被拒绝</b>并提示收窄条件，
    /// 而不是静默截断 —— 审计导出是证据类产物，「那段时间的全部记录」与「前 N 条」在合规上不是一回事，
    /// 而一份被砍掉尾巴、外观却完整的文件没有任何地方能看出来。
    /// </summary>
    [RuntimeSetting(Label = "Export Row Limit", I18n = "admin.modules.system.settings.fields.auditExportMaxRows",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum number of audit operations a single CSV / JSON export may contain. An export whose filter matches more rows is refused with guidance to narrow the filter; it is never silently truncated")]
    public int ExportMaxRows { get; set; } = 10000;

    /// <summary>
    /// 排除的请求路径前缀。
    /// "/hubs" 仍要排除：WS 长连接在断开时才入队，产生耗时数小时的无意义"操作"记录
    /// （它的 access_token 查询参数如今由 <see cref="SensitiveQueryKeys"/> 脱敏，不再依赖路径排除）。
    /// </summary>
    public string[] ExcludedPaths { get; set; } = ["/swagger", "/health", "/scalar", "/hubs"];

    /// <summary>
    /// 查询串里要脱敏的参数名（不区分大小写，精确匹配）。作用于 <c>Url</c>（Path + QueryString）
    /// 与 <c>RequestParameters</c> 两列：命中的参数值一律记为 <c>***</c>，其余原样保留。
    /// </summary>
    /// <remarks>
    /// ★ 初值取自核心的 <see cref="QueryStringRedactor.DefaultSensitiveKeys"/>，与 <c>Tnzi.AspNetCore</c>
    /// 的请求日志同源 —— 框架自己就会把凭据放进查询串：SignalR 的 <c>access_token</c>、签名文件链接的
    /// <c>sig</c>、分享链接口令 <c>password</c>、邮件里重置 / 确认链接的 <c>token</c>、passkey 注册的
    /// <c>enrollmentToken</c>。此前审计表只靠 <see cref="ExcludedPaths"/> 挡住 <c>/hubs</c>，
    /// 其余端点的令牌原值一行行落进了 <c>Audit_Operation.Url</c>；路径排除盖不住这些端点，
    /// 因为它们的请求本身正是要审计的操作。
    /// 与 <see cref="SensitiveFields"/> 刻意是两份名单：请求体字段名走 camelCase（<c>accessToken</c>），
    /// 查询参数名走 OAuth 的 snake_case（<c>access_token</c>）。部署方按自己的参数名增删。
    /// </remarks>
    public HashSet<string> SensitiveQueryKeys { get; set; } =
        new(QueryStringRedactor.DefaultSensitiveKeys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Channel 容量 (0 = 无限)。
    /// ★ 队列满时<b>丢弃新来的记录</b>并记 Error 日志（第一次与之后每 <c>AuditSender.DropLogInterval</c> 条各一条），
    /// 累计丢弃数经 <c>AuditSender.DroppedCount</c> 暴露。数据库变慢或一波流量让消费落后时，审计表会少一段 ——
    /// 这一条保证少的那一段至少看得见。
    /// </summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>是否记录请求参数</summary>
    [RuntimeSetting(Label = "Capture Request Parameters", I18n = "admin.modules.system.settings.fields.auditEnableRequestParameters",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Record query string / form parameters on each audited operation")]
    public bool EnableRequestParameters { get; set; } = true;

    /// <summary>
    /// 是否记录响应结果。
    /// KEEP-STATIC：当前无运行时消费者（AuditMiddleware 未读取此开关），暴露会造成"假热配"。
    /// </summary>
    public bool EnableResponseResult { get; set; } = false;

    /// <summary>是否启用请求体记录</summary>
    [RuntimeSetting(Label = "Capture Request Body", I18n = "admin.modules.system.settings.fields.auditEnableRequestBodyCapture",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Persist request bodies to the audit table. May store sensitive payloads (PII, credentials); sensitive fields are redacted, but review privacy impact before enabling in production")]
    public bool EnableRequestBodyCapture { get; set; } = false;

    /// <summary>请求体最大记录大小（字节），超出部分截断</summary>
    [RuntimeSetting(Label = "Max Request Body Size (bytes)", I18n = "admin.modules.system.settings.fields.auditMaxRequestBodySize",
        Type = SettingFieldType.Int, Min = 1, Max = 65536, Subsection = "Capture",
        Description = "Request bodies larger than this size (bytes) are truncated")]
    public int MaxRequestBodySize { get; set; } = 4096;

    /// <summary>
    /// 需要脱敏的敏感字段名（不区分大小写，精确匹配）。
    /// 同时作用于请求体 JSON 字段（<see cref="RequestBodyRedactor"/>）与实体级审计的属性名
    /// （EntityAuditSaveChangesInterceptor，记录"变了"但值打码）。
    /// PasswordHash/SecurityStamp 覆盖 IdentityUser 继承属性——它们无法打
    /// [AuditIgnore]（属性定义在 ASP.NET Core Identity 基类上）。
    /// </summary>
    /// <remarks>
    /// ★ 初值取自核心的 <see cref="RequestBodyRedactor.DefaultSensitiveFields"/>，
    /// 与 <c>Tnzi.AspNetCore</c> 的请求日志共用同一份名单 —— 两处各维护一份的结果是
    /// 「某个流程比别处多露出一个字段」，而那不报错也不会让测试变红。
    /// 这里仍是可写集合：部署方按自己的业务字段增删。
    /// </remarks>
    public HashSet<string> SensitiveFields { get; set; } =
        new(RequestBodyRedactor.DefaultSensitiveFields, StringComparer.OrdinalIgnoreCase);
}
