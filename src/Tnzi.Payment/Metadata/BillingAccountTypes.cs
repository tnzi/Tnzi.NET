namespace Tnzi.Payment.Metadata;

/// <summary>
/// 计费账户的种类：一笔支付、一张已保存的卡、一条订阅、一张发票归谁。
/// </summary>
/// <remarks>
/// 存进 <c>BillingAccountType</c> 列的是这几个字符串本身（不是枚举数值）：
/// 消费方注册自己的 <see cref="Services.IBillingAccountResolver"/> 时可以用别的种类（例如「部门」），
/// 列宽 <see cref="BillingAccountRef.TypeMaxLength"/> 以内即可。
/// </remarks>
public static class BillingAccountTypes
{
    /// <summary>个人：账户就是这个用户自己（默认，与引入计费账户之前逐字相同）。</summary>
    public const string User = "User";

    /// <summary>整个安装：一套部署只服务一个客户，所有付款都记在同一个账户上。</summary>
    public const string Installation = "Installation";

    /// <summary>租户：每个租户一个账户（租户即客户的多租户部署）。</summary>
    public const string Tenant = "Tenant";
}

/// <summary>
/// 内置解析器按什么划分计费账户（<c>Payment:BillingAccount:Scope</c>）。
/// </summary>
public enum BillingAccountScope
{
    /// <summary>每个用户一个账户，自己付自己的（默认）。</summary>
    User = 0,

    /// <summary>整个安装一个账户：单客户部署，由持有 <c>payment.account.manage</c> 的人共同管理。</summary>
    Installation = 1,

    /// <summary>每个租户一个账户：租户即客户，由该租户里持有 <c>payment.account.manage</c> 的人共同管理。</summary>
    Tenant = 2
}

/// <summary>
/// 一个计费账户的标识：种类 + 该种类下的 Id。
/// </summary>
/// <remarks>
/// <para>
/// ★ 刻意是两列字符串而不是某个实体的外键：账户可以是一个用户、一整个安装、一个租户，
/// 也可以是消费方自己的什么东西：同 <c>Tnzi.Signing</c> 用 <c>HostEntityType + HostEntityId</c>
/// 绑多态宿主的形状。它回答的是「这笔钱算谁的」，不是「谁点的按钮」：后者仍是各实体上的 <c>UserId</c>（经办人）。
/// </para>
/// <para>
/// ★ <see cref="Id"/> 按写下时的样子做等值比较。用户账户一律写 <see cref="Guid.ToString()"/>（小写带短横），
/// 既有数据回填时必须产出同一形态（SQL Server 的 <c>CONVERT(nvarchar(36), uniqueidentifier)</c> 是大写，要 <c>LOWER</c>），
/// 否则回填过的行在区分大小写的比较下谁都找不到。
/// </para>
/// </remarks>
public sealed record BillingAccountRef
{
    /// <summary><c>BillingAccountType</c> 列宽。</summary>
    public const int TypeMaxLength = 32;

    /// <summary><c>BillingAccountId</c> 列宽。</summary>
    public const int IdMaxLength = 64;

    /// <summary>账户种类，见 <see cref="BillingAccountTypes"/>。</summary>
    public string Type { get; }

    /// <summary>该种类下的账户 Id。</summary>
    public string Id { get; }

    public BillingAccountRef(string type, string id)
    {
        Type = Check.NotNullOrWhiteSpace(type, nameof(type));
        Id = Check.NotNullOrWhiteSpace(id, nameof(id));

        if (Type.Length > TypeMaxLength)
            throw new ArgumentException($"Billing account type cannot exceed {TypeMaxLength} characters.", nameof(type));
        if (Id.Length > IdMaxLength)
            throw new ArgumentException($"Billing account id cannot exceed {IdMaxLength} characters.", nameof(id));
    }

    /// <summary>某个用户的个人账户。</summary>
    public static BillingAccountRef ForUser(Guid userId) => new(BillingAccountTypes.User, userId.ToString());

    /// <summary>从两列读回来；任一为空返回 <see langword="null"/>（没有归属的旧行或匿名付款）。</summary>
    public static BillingAccountRef? From(string? type, string? id)
        => string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id) ? null : new BillingAccountRef(type, id);

    /// <summary>
    /// 写进渠道侧元数据（Stripe customer 的 <c>BillingAccount</c>）的形态：<c>{Type}:{Id}</c>。
    /// </summary>
    public string Key => $"{Type}:{Id}";

    /// <summary>这是不是某个用户的个人账户；是则给出用户 Id。</summary>
    public bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        return string.Equals(Type, BillingAccountTypes.User, StringComparison.Ordinal) && Guid.TryParse(Id, out userId);
    }

    public override string ToString() => Key;
}

/// <summary>
/// 一个用户在当前请求上下文里代表哪个计费账户，以及他对这个账户能做什么。
/// </summary>
/// <param name="Account">他代表的账户。</param>
/// <param name="CanView">能否查看账户的订阅、付款、发票与已保存的卡。</param>
/// <param name="CanManage">能否代表账户付款、开通 / 变更 / 取消订阅、增删卡片、申请退款。</param>
public sealed record BillingAccountAccess(BillingAccountRef Account, bool CanView, bool CanManage);
