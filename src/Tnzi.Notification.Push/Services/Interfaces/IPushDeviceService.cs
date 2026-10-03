namespace Tnzi.Notification.Push.Services;

/// <summary>
/// 设备令牌注册表：回答「发给这个主体要投到哪几台设备」。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>契约与实现都在本模块，父模块 <c>Tnzi.Notification</c> 一无所知。</b>
/// 不是每个应用都发推送 —— 把这张表或它的契约放进父模块，只发邮件的应用会凭空多出
/// 一张永远为空的表和一个死端点，而那正是当初把推送拆出来的理由。
/// </para>
/// <para>
/// 代价是消费方要显式走两步：先解析收件人、再建通知。这是刻意的 ——
/// 没有加载本模块时 <see cref="ResolveRecipientsAsync"/> 的<b>调用点直接编译不过</b>，
/// 不存在「安静地少发了一批人」的窗口。同一取舍见 <c>Tnzi.Authorization.DataAuth</c>：
/// 它同样刻意不在父模块留契约。
/// </para>
/// <para>
/// ★ <b>两组方法对应两种主体。</b>登录用户那组（<see cref="RegisterAsync"/> /
/// <see cref="UnregisterByTokenAsync"/> / <see cref="GetMyDevicesAsync"/> /
/// <see cref="ResolveRecipientsAsync"/>）靠当前登录态认人；匿名那组
/// （<see cref="RegisterAnonymousAsync"/> / <see cref="UnregisterAnonymousAsync"/> /
/// <see cref="ResolveByDeviceIdsAsync"/>）靠服务端签发的设备密钥认设备，服务的是没有账号
/// 体系的 App。两组共用同一张表，一行可以同时挂着两种身份。
/// </para>
/// <para>
/// ★ 匿名那组的<b>注册与刷新是同一个方法</b>（带没带密钥决定是哪一种），这样客户端不必
/// 判断「这是不是首次」—— 那个判断依据是「本地存储里有没有」，而读存储失败与真的首次
/// 在客户端看来一模一样。
/// </para>
/// <para>
/// ★ <b>只使用主题广播的应用不需要碰本服务</b>：那条路一个设备标识符都不存。
/// 匿名 p2p 与主题广播的分界见 <see cref="Entities.PushDevice"/> 的类注释。
/// </para>
/// </remarks>
public interface IPushDeviceService
{
    /// <summary>
    /// 注册（或刷新）当前登录用户的一个推送令牌。
    /// </summary>
    /// <remarks>
    /// upsert 语义，按<b>令牌</b>定位而不是按用户：令牌已挂在别人名下时改挂到当前用户
    /// —— 一台设备换人登录，旧用户必须立刻停止收到推送。
    /// <para>
    /// ★ <b>不动那一行的匿名身份</b>：这台设备可能是先以匿名身份提交过表单、
    /// 用户后来才注册账号的，清掉它就会让那些表单的回执静默停发。两种身份并存。
    /// </para>
    /// </remarks>
    Task<Result<PushDeviceDto>> RegisterAsync(RegisterPushDeviceDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按<b>令牌</b>注销当前登录用户名下的这台设备 —— 退出登录时调用这个。
    /// </summary>
    /// <remarks>
    /// ★ <b>登出时客户端手里只有令牌，没有行 id。</b>它可以在注册时把
    /// <see cref="PushDeviceDto.Id"/> 存下来，但那要求客户端为此多维护一份本地状态，
    /// 而重装或令牌轮换后那份状态就失效了；设备列表里的令牌又是<b>掩码</b>，
    /// 反查不回来。所以按令牌注销不是「另一个便利重载」，而是登出路径上<b>唯一</b>走得通的那条。
    /// <para>
    /// 归属仍然限定在当前用户：令牌在这中间被改挂给别人时（那台设备换人登录了），
    /// 这里不该把新主人的那一行删掉。
    /// </para>
    /// <para>
    /// ★ 这一行若还挂着匿名身份，<b>只解除用户归属，不删行</b>：登出的是人，不是设备。
    /// </para>
    /// </remarks>
    Task<Result> UnregisterByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按<b>行 id</b> 注销当前登录用户名下的一台设备 —— 设备列表里点「移除这台」用这个。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="UnregisterByTokenAsync"/> 分工不同：那条服务「我这台要登出」，
    /// 这条服务「我在设备列表里踢掉另一台」—— 后者手上有 id 没有令牌，正好相反。
    /// </remarks>
    Task<Result> UnregisterAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>列出当前登录用户的全部已注册设备。</summary>
    Task<Result<List<PushDeviceDto>>> GetMyDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>admin 端分页查询设备清单。</summary>
    Task<Result<IPagedList<PushDeviceDto>>> GetPagedListAsync(PushDeviceQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>admin 端删除一台设备。</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册或刷新一台<b>没有账号</b>的设备。注册与刷新是同一个方法。
    /// </summary>
    /// <param name="deviceKey">
    /// 客户端手上的设备密钥；<see langword="null"/> 表示它还没有身份，本次为它签发一枚。
    /// </param>
    /// <param name="input">上报的令牌与平台，可选带上一个辨认用的设备标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>
    /// <para>
    /// ★★ <b>合成一个方法，是为了让客户端不必判断「这是不是首次」。</b>手上有密钥就带上，
    /// 响应里有密钥就存下来 —— 客户端因此不需要在请求侧写控制流分支。拆成注册 / 刷新两条时，
    /// 它的判断依据是「本地存储里有没有」，而<b>读存储失败与真的首次在客户端看来一模一样</b>，
    /// 走错分支的后果是签发一个新身份、顶掉旧的，此前那些记录的回执就断了。
    /// </para>
    /// <para>
    /// 签发出的密钥<b>明文只在返回值里出现这一次</b>（库里只留哈希），客户端必须存进平台的
    /// 安全存储；带着密钥来刷新时 <see cref="AnonymousDeviceRegistrationDto.DeviceKey"/> 为空。
    /// 设备 Id 由密钥派生而不是随机生成，理由见 <c>AnonymousDeviceId</c>：它是消费方业务记录里
    /// 的归属键，要活得比推送令牌久。
    /// </para>
    /// <para>
    /// ★ <b>带来的密钥查不到对应的行时会就地重建，而不是报错。</b>令牌被网关判死时整行会被
    /// <see cref="RetireAsync"/> 删掉，但密钥仍然有效、派生出的 Id 与当初签发的<b>逐字相同</b>，
    /// 于是重建之后此前那些记录的回执照发。若这里改成 401 要求重新注册，客户端会拿到一个新 Id，
    /// 那些回执就永久断了。
    /// </para>
    /// <para>
    /// ★ 服务端<b>不接受客户端上报的设备 Id</b>，一律从密钥现算：Id 是公开值，谁都能报别人的。
    /// 同理 <see cref="RegisterPushDeviceDto.ExternalDeviceId"/> 只作为辨认信息存下来，
    /// <b>一处寻址都不参与</b>；本接口刻意没有「按它解析收件人」的方法。
    /// </para>
    /// <para>
    /// ★ <b>令牌已被别的行占着时，那一行会被顶掉，但它的登录归属带过来不丢。</b>
    /// 令牌是设备的地址，同一时刻只能属于一行。而一台设备重装 App 会拿到<b>新的</b>令牌，
    /// 所以「令牌没变、身份换了」几乎必然还是同一台设备的同一个安装（清掉了本地密钥，
    /// 或者从登录模式切到了匿名模式），不是换了人 —— 把 UserId 一起搬过来，才不会让一个
    /// 还登录着的用户在这台设备上静默失去推送。顶掉的代价是<b>旧的匿名 Id 失效</b>，
    /// 此前那些记录的回执发不出去；客户端弄丢密钥就是弄丢了身份，框架没有找回入口，
    /// 那种入口本身就是一条绕开密钥的旁路。
    /// </para>
    /// <para>
    /// ★ <b>这是一个匿名可写的入口，宿主必须为它配限流。</b>框架不内置限流策略
    /// （那要求宿主已经装好 rate limiter，没装的话端点直接抛异常），
    /// 所以这一条落在部署方身上，见 docs/modules/notification-push.md。
    /// </para>
    /// </remarks>
    Task<Result<AnonymousDeviceRegistrationDto>> RegisterAnonymousAsync(
        string? deviceKey, RegisterPushDeviceDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 凭设备密钥注销一台匿名设备（用户在 App 里关掉推送，或清除本机数据前）。
    /// </summary>
    /// <remarks>
    /// 幂等：密钥对应的行已经不在（重复调用，或它已被投递路径判定失效而退役）也返回成功。
    /// 「已经不再收推送」是正确状态，不该长得像一次失败。
    /// <para>
    /// 这一行若还挂着登录用户，<b>只清掉匿名身份，不删行</b> —— 关掉的是设备的匿名投递，
    /// 不是那个用户的设备。
    /// </para>
    /// <para>
    /// ★ 参数可空，与 <see cref="RegisterAnonymousAsync"/> 同一形状：密钥来自请求头，
    /// 而请求头天然可能没有。让签名说真话，调用点就不需要一个 <c>!</c> 去掩盖它 ——
    /// 缺失在这里有明确答复（401），不是一个待排除的意外。
    /// </para>
    /// </remarks>
    Task<Result> UnregisterAnonymousAsync(string? deviceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一组用户展开成推送收件人，一台设备一条。
    /// </summary>
    /// <remarks>
    /// 返回 <see cref="PushRecipientResolution"/> 而不是扁平列表：没有任何已注册设备的用户
    /// 必须被单独报出来，否则他们只是<b>不出现在</b>投递记录里，与「这次本来就没发给他们」
    /// 无法区分。调用方按自己的业务决定这算不算失败。
    /// <para>
    /// 返回原始类型而不是 <c>Result</c>：它由消费方的服务层调用而不是控制器，
    /// 遵循 docs/coding-standards 的返回类型分层。
    /// </para>
    /// </remarks>
    Task<PushRecipientResolution> ResolveRecipientsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一组<b>设备 Id</b> 展开成推送收件人，一台设备一条 —— 匿名 p2p 走这条。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 产出的收件人 <c>UserId</c> 留空。这不是省事：投递管线的偏好过滤与频次上限
    /// <b>对无主收件人原样放行</b>（两者都按人算，而匿名设备没有人），
    /// 退订仍然按地址生效 —— 也就是说匿名设备照样退得掉。
    /// </para>
    /// <para>
    /// 找不到的设备 Id 必须报出来，见 <see cref="PushDeviceResolution"/>。
    /// </para>
    /// </remarks>
    Task<PushDeviceResolution> ResolveByDeviceIdsAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 退役一个已被推送网关判定为永久失效的令牌，返回删除的行数。
    /// </summary>
    /// <remarks>
    /// ★ 由 <see cref="PushSender"/> 在 FCM 回 <c>Unregistered</c>（以及只有一个 Firebase 项目的部署里的
    /// <c>SenderIdMismatch</c>）时调用 —— 那是这张表<b>唯一</b>能得知令牌已死的时机：FCM 不会主动通知，
    /// 客户端卸载了 App 也不会来注销。不退役的话表只增不减，而 admin 的
    /// 「重试失败项」会对着死令牌一直重试。
    /// <para>
    /// 返回原始类型：它是投递路径上的收尾动作，失败不该影响那次投递的结论。
    /// </para>
    /// <para>
    /// ★ 匿名行也照删不误。客户端下次启动带着密钥回来时 <see cref="RegisterAnonymousAsync"/>
    /// 会用<b>同一个 Id</b> 把它重建出来，所以删掉不等于丢掉那台设备的身份。
    /// </para>
    /// </remarks>
    Task<int> RetireAsync(string token, CancellationToken cancellationToken = default);
}
