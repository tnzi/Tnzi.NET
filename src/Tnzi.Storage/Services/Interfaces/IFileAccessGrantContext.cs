namespace Tnzi.Storage.Services;

/// <summary>
/// 本次请求内「已经被别的凭据授权过」的文件集合。
///
/// 存在的理由:分享链接的凭据是**令牌本身**,不是调用者的身份 —— 收件人往往根本没有账号。
/// 但读判定必须留在服务层(控制器是 <c>[DefaultController]</c>,消费方可以整体替换掉),
/// 所以校验分享的那个服务把结论**放进请求作用域**,由 <see cref="IFileAccessAuthorizer"/> 读取。
///
/// 边界:
/// <list type="bullet">
/// <item>只影响**读取**。授予不给写权限,也不能拿去签发访问令牌 ——
/// 否则一条限次数、会过期的分享链接就能被换成一张不受这些约束的令牌。</item>
/// <item>作用域是**一次请求**。它不落库、不跨请求,所以不存在"忘了撤销"的状态。</item>
/// <item>只有确实校验通过了某种凭据的服务才该调用 <see cref="Grant"/>。</item>
/// </list>
/// </summary>
public interface IFileAccessGrantContext
{
    /// <summary>
    /// 记录:本次请求已凭其它凭据(分享令牌等)取得该文件的读取权。
    /// </summary>
    void Grant(Guid fileId);

    /// <summary>
    /// 本次请求是否已被授予该文件的读取权。
    /// </summary>
    bool IsGranted(Guid fileId);
}

/// <summary>
/// 默认实现。Scoped,状态就是一个 HashSet —— 一次请求结束即随作用域消失。
/// </summary>
public class FileAccessGrantContext : IFileAccessGrantContext
{
    private readonly HashSet<Guid> _granted = [];

    public void Grant(Guid fileId) => _granted.Add(fileId);

    public bool IsGranted(Guid fileId) => _granted.Contains(fileId);
}
