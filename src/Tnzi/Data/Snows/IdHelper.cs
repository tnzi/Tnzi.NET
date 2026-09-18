namespace Tnzi.Data.Snows;

/// <summary>
/// 进程级雪花 ID 入口。
/// </summary>
/// <remarks>
/// ★ 机器码是部署参数：由 <c>CoreServicesModule</c> 在 ConfigureServices 阶段按 <c>IdGeneration</c> 配置节
/// （<see cref="IdGenerationOptions"/>）调用 <see cref="SetIdGenerator"/>，模块化应用永远不会走到
/// <see cref="NextId"/> 的惰性回退。未配置机器码时 Production 启动即失败，其它环境记 Warning 用默认值 1。
/// 惰性回退只服务于不经模块系统的调用方（单元测试、脚本）：那是单进程场景，固定 WorkerId=1 在那里是安全的；
/// 在多实例部署里它就是「两个副本同一毫秒产出相同 long」的根源，所以真实应用不能依赖它。
/// </remarks>
public class IdHelper
{
    private static volatile IIdGenerator? _IdGenInstance;
    private static readonly object _initLock = new();

    public static IIdGenerator IdGenInstance => _IdGenInstance ?? throw new InvalidOperationException("IdGenerator has not been initialized. Call SetIdGenerator first.");

    /// <summary>
    /// 获取一个值, 指示 Id 生成器是否已初始化
    /// </summary>
    public static bool IsInitialized => _IdGenInstance != null;

    /// <summary>
    /// 设置参数, 建议程序初始化时执行一次
    /// </summary>
    /// <param name="options"></param>
    public static void SetIdGenerator(IdGeneratorOptions options)
    {
        lock (_initLock)
        {
            _IdGenInstance = new DefaultIdGenerator(options);
        }
    }

    /// <summary>
    /// 生成新的Id
    /// 模块化应用由 <c>CoreServicesModule</c> 按 <c>IdGeneration</c> 配置节初始化生成器；
    /// 未初始化时惰性回退成 WorkerId=1（仅单进程安全，见类注释）。
    /// </summary>
    /// <returns></returns>
    public static long NextId()
    {
        if (_IdGenInstance == null)
        {
            lock (_initLock)
            {
                _IdGenInstance ??= new DefaultIdGenerator(
                    new IdGeneratorOptions() { WorkerId = 1 });
            }
        }

        return _IdGenInstance.NewLong();
    }

}

