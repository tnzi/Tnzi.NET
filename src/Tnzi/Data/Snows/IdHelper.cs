namespace Tnzi.Data.Snows;

/// <summary>
/// 这是一个调用的例子, 默认情况下, 单机集成者可以直接使用 NewId()。
/// </summary>
public class IdHelper
{
    private static IIdGenerator? _IdGenInstance = null;

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
        _IdGenInstance = new DefaultIdGenerator(options);
    }

    /// <summary>
    /// 生成新的Id
    /// 调用本方法前, 请确保调用了 SetIdGenerator 方法做初始化。
    /// 否则将会初始化一个WorkerId为1的对象。
    /// </summary>
    /// <returns></returns>
    public static long NextId()
    {
        if (_IdGenInstance == null)
        {
            _IdGenInstance = new DefaultIdGenerator(
                new IdGeneratorOptions() { WorkerId = 1 }
                );
        }

        return _IdGenInstance.NewLong();
    }

}

