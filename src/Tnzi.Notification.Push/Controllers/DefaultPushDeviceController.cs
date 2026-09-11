namespace Tnzi.Notification.Push.Controllers;

/// <summary>
/// 推送设备注册端点：登录用户的设备，以及没有账号的匿名设备。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>路由模板刻意与父模块的 <c>notifications</c> 不同。</b>
/// <c>ModuleControllerReplacementProvider</c> 按<b>精确路由模板</b>分组去重，
/// 所以消费方覆写父模块的通知控制器不会连带删掉本模块这几个端点。
/// 参见 CLAUDE.md「控制器替换是定向的」。
/// </para>
/// <para>
/// 登录那组端点只作用于<b>当前登录用户自己</b>的设备，因此不需要额外的权限码 ——
/// 类级 <c>[ApiAuthorize]</c> 要求已认证即可，归属由服务层强制。
/// </para>
/// <para>
/// ★ <b>匿名那两个挂 <c>[AllowAnonymous]</c> 逐个豁免类级检查</b>，服务的是没有账号体系的 App
/// （典型形态：设备提交表单，处理完把回执推回这台设备）。它们凭服务端签发的<b>设备密钥</b>
/// 认设备，密钥走 <c>X-Device-Key</c> 请求头。注册与刷新刻意是<b>同一个端点</b>，
/// 见 <see cref="RegisterAnonymous"/>。
/// </para>
/// <para>
/// ★★ <b>这两个是匿名可写的入口，宿主必须为它们配限流。</b>框架刻意不在这里硬编码一个
/// <c>[EnableRateLimiting]</c> 策略：那要求宿主已经装好了同名策略，没装的话端点在运行期
/// 直接抛异常 —— 一个为了防灌表而加的东西，反倒让没配它的部署整条注册路径不可用。
/// 落地做法见 docs/modules/notification-push.md。
/// </para>
/// </remarks>
[DefaultController]
[ApiAuthorize]
[Route("notifications/devices")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultPushDeviceController : ApiControllerBase
{
    protected readonly IPushDeviceService DeviceService;

    public DefaultPushDeviceController(IPushDeviceService deviceService)
    {
        DeviceService = Check.NotNull(deviceService);
    }

    /// <summary>本次请求带来的匿名设备密钥；没带则为 <see langword="null"/>。</summary>
    protected string? DeviceKey => Request.Headers[PushDeviceHeaders.DeviceKey].FirstOrDefault();

    /// <summary>
    /// 上报当前设备的推送令牌。
    /// </summary>
    /// <remarks>
    /// upsert 语义，客户端<b>每次启动</b>拿到令牌就调一次即可：FCM 会在重装、清数据、
    /// 令牌轮换时换发新令牌，而客户端无从知道服务端见没见过它。
    /// </remarks>
    [HttpPost]
    public virtual async Task<ApiResult<PushDeviceDto>> Register([FromBody] RegisterPushDeviceDto input)
        => (await DeviceService.RegisterAsync(input)).ToApiResult();

    /// <summary>列出当前用户的已注册设备。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<PushDeviceDto>>> GetMine()
        => (await DeviceService.GetMyDevicesAsync()).ToApiResult();

    /// <summary>
    /// 按令牌注销这台设备 —— <b>登出时调这个</b>。
    /// </summary>
    /// <remarks>
    /// ★ 用 POST + 请求体而不是 <c>DELETE ?token=</c>：查询串会原样进访问日志与
    /// 反向代理日志，而令牌本身就是「哪台设备装了这个 App」那条事实的载体。
    /// <para>
    /// 幂等：令牌不存在（重复登出，或它已被投递路径判定失效而退役）也返回成功 ——
    /// 「已经不再收推送」是正确状态，不该长得像一次失败。
    /// </para>
    /// </remarks>
    [HttpPost("unregister")]
    public virtual async Task<ApiResult> Unregister([FromBody] UnregisterPushDeviceDto input)
        => (await DeviceService.UnregisterByTokenAsync(input?.Token!)).ToApiResult();

    /// <summary>
    /// 按行 id 移除当前用户的某台设备 —— 设备列表里点「移除这台」用这个。
    /// </summary>
    /// <remarks>
    /// 与上面那条分工不同：这条手上有 id 没有令牌（在列表里挑另一台），
    /// 登出那条手上有令牌没有 id。列表里的令牌是掩码，反查不回来。
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Remove(Guid id)
        => (await DeviceService.UnregisterAsync(id)).ToApiResult();

    /// <summary>
    /// 注册或刷新这台没有账号的设备 —— <b>每次启动都调这一个</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 客户端一共三步，没有分支：
    /// <code>
    /// key = secureStorage.get('pushDeviceKey')          // 可能没有
    /// r   = POST anonymous { token, platform } + (key ? X-Device-Key : none)
    /// if (r.deviceKey) secureStorage.set('pushDeviceKey', r.deviceKey)
    /// </code>
    /// 之后把 <c>r.deviceId</c> 随业务请求一起提交，服务端按它把回执推回这台设备。
    /// </para>
    /// <para>
    /// ★ <b>注册与刷新是同一个端点</b>，带没带 <c>X-Device-Key</c> 决定是哪一种。拆成两个端点，
    /// 客户端就得判断「这是不是首次」，而它的依据是「本地存储里有没有」——
    /// <b>读存储失败与真的首次在客户端看来一模一样</b>，走错分支就会签发一个新身份、
    /// 顶掉旧的，此前那些记录的回执从此发不出去。
    /// </para>
    /// <para>
    /// ★ <c>deviceKey</c> <b>只在签发那一次返回</b>，客户端必须立刻存进平台的安全存储
    /// （iOS Keychain / Android Keystore）。丢了就是丢了这台设备的身份，服务端<b>没有</b>
    /// 找回入口 —— 那种入口本身就是一条绕开密钥的旁路。
    /// </para>
    /// <para>
    /// ★ <c>deviceId</c> <b>每次都返回</b>，所以客户端不需要额外存它。
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("anonymous")]
    public virtual async Task<ApiResult<AnonymousDeviceRegistrationDto>> RegisterAnonymous(
        [FromBody] RegisterPushDeviceDto input)
        => (await DeviceService.RegisterAnonymousAsync(DeviceKey, input)).ToApiResult();

    /// <summary>
    /// 凭 <c>X-Device-Key</c> 注销这台匿名设备（用户在 App 里关掉推送、或清除本机数据前）。
    /// </summary>
    /// <remarks>
    /// 幂等：密钥对应的行已经不在也返回成功。这同时让「密钥不对」与「密钥对但那一行已退役」
    /// 回答同一句话 —— 区分开就是在帮人试探哪些密钥是真的。
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("anonymous/unregister")]
    public virtual async Task<ApiResult> UnregisterAnonymous()
        => (await DeviceService.UnregisterAnonymousAsync(DeviceKey)).ToApiResult();
}
