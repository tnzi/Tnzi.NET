namespace Tnzi.Identity.Services;

/// <summary>
/// 一个账号正被身份模块之外的记录当作主体使用的事实。
/// </summary>
/// <param name="Detail">
/// 面向操作员的说明：<b>是什么</b>在用这个账号，以及该改做什么（通常是「先在那边办离职 / 退役，
/// 再删账号」）。它会原样成为删除端点 409 的消息，所以要写成一句人能照着做的话。
/// </param>
/// <remarks>
/// 刻意不带 wire 原因码：这个答案唯一的去处是删除端点的 409 消息。等到真的有呈现端要按原因分支时，
/// 再给这个 record 加一个属性即可（positional record 追加属性不破坏既有调用方）。
/// </remarks>
public sealed record UserUsage(string Detail);

/// <summary>
/// 回答「这个账号，除了身份模块自己的表之外，还有别的记录以它为主体吗？」
/// </summary>
/// <remarks>
/// <b>为什么存在</b>：消费应用惯常的形状是「一条领域记录 + 一个框架登录」—— 员工档案、警员台账、
/// 薪酬主数据各自持有一个可空的 <c>UserId</c> 松引用 <see cref="Entities.User"/>，并且**刻意不建外键**：
/// 删一个账号是不是要连带删掉那条档案，是领域模块的决定，不该由一条级联规则替它做。
/// 代价是框架自带的 <c>DELETE admin/users/{id}</c> 对这些行一无所知：删完之后它们指着一个已删账号，
/// 列表照常、状态正常，只是这个人再也登不进来，而且没有任何东西会报错。
/// <br/><br/>
/// 这个契约让持有那条记录的模块在删除**之前**说话。语义与 Finance 的主数据删除守卫相同：
/// <b>只读、只回答、不代为清理</b>。守卫拒绝时一律指路让操作员自己去处理引用（通常是先在领域侧
/// 办离职 / 退役，那一步会由领域模块自己决定要不要顺手删账号）—— 替他把引用悄悄改掉，
/// 是在无声地篡改别人的记录。
/// <br/><br/>
/// <b>可选</b>：未注册任何实现时视为「没有别人在用」，删除路径回到引入本契约之前的行为。
/// 这是唯一安全的缺省：本契约只会<b>增加</b>拒绝，不会放宽任何既有检查。
/// <br/><br/>
/// 多个实现可并存（<c>IEnumerable</c> 注入）：一个宿主里员工档案与警员台账可以各自认领各自的账号。
/// 任意一个回答「在用」即拒绝，第一个非 null 的答案就是给操作员的说明。
/// <br/><br/>
/// ★ <b>注册了本契约的模块，自己「连账号一起删」的路径要先软删自己的行、flush、再调
/// <c>IUserService.DeleteAsync</c></b>：反过来先删账号，那一问会问到它自己的实现，行还在就答「在用」，
/// 模块用 409 拒绝了自己的删除。实现经同一个 DbContext 读活行，已 flush 的软删在同一事务里对它可见。
/// 第二步失败时行不能单独留在已删状态：全局工作单元下失败的 <c>Result</c> 信封会让请求级事务整体回滚，
/// 手工 <c>ExecuteInUnitOfWorkAsync</c> 只在异常时回滚，那条路径上第二步失败要抛出。
/// <br/><br/>
/// 想在删除**之后**善后（清缓存、撤销领域侧的席位）的模块，听 <see cref="Events.UserDeletedEvent"/>，
/// 那是另一件事：本契约决定删不删，事件通知已经删了。
/// </remarks>
public interface IUserUsageProvider
{
    /// <summary>
    /// 这个账号被本实现所辖的记录当作主体了吗？
    /// </summary>
    /// <param name="userId">账号 Id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>在用时返回可展示的说明；不在用返回 <c>null</c></returns>
    /// <remarks>
    /// 入参是<b>单个</b>账号，实现应当用存在性查询（<c>AnyAsync</c>）作答，不要把引用者物化出来。
    /// 已经软删的领域记录算不算「在用」由实现决定：一份已归档的员工档案通常仍然要留着它的
    /// <c>UserId</c>（审计要能回答「当时是谁」），这种情况该答「在用」并指路去归档流程。
    /// </remarks>
    Task<UserUsage?> FindUsageAsync(Guid userId, CancellationToken cancellationToken = default);
}
