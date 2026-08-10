using System.Reflection;

namespace Tnzi.TestBase;

/// <summary>
/// 源码扫描类门禁的仓库根定位器 —— 定位不到就<b>抛异常</b>，绝不返回 null。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是一次实测到的假绿。此前每个扫源码的门禁都自带一份
/// <c>FindRepoRoot()</c>（从 <c>AppContext.BaseDirectory</c> 向上找 <c>Tnzi.NET.slnx</c>），
/// 找不到就 <c>return</c>，注释写着「打包/隔离环境下跳过，不误报」。后果是：
/// 只要测试产物不在仓库树内，<b>12 个门禁会一个不落地静默通过</b> ——
/// <c>Tnzi.Architecture.Tests</c> 会打印 <c>Passed! 11/11</c> 而一行源码都没扫，
/// <c>Tnzi.Tests</c> 的命名空间与文件长度约定同理。
/// </para>
/// <para>
/// 而「测试产物不在仓库树内」并不是什么边缘情况：本仓库自己的验证手册就推荐用
/// <c>-p:BaseOutputPath=&lt;临时目录&gt;/</c> 绕开 in-tree 应用常驻造成的 MSB3021 文件锁 ——
/// 那条推荐做法恰好会把这批门禁全部变成空操作，而输出里看不出任何区别。
/// <b>能工作的降级路径比报错危险得多：报错会被人看见，静默通过不会。</b>
/// </para>
/// <para>
/// 因此这里做两件事：①主路径改用<b>编译期</b>注入的程序集元数据，天然不受输出目录搬迁影响；
/// ②两条路径都失败时抛出，让「门禁跑不了」与「门禁通过」不再是同一个观测结果。
/// </para>
/// </remarks>
public static class RepoRoot
{
    /// <summary>仓库根的判定标志物。</summary>
    private const string MarkerFile = "Tnzi.NET.slnx";

    /// <summary>编译期由 csproj 注入的仓库根，见 <c>Tnzi.TestBase.csproj</c> 的 AssemblyMetadata。</summary>
    private const string MetadataKey = "TnziRepoRoot";

    private static readonly Lazy<string> Cached = new(Resolve);

    /// <summary>返回仓库根的绝对路径；定位不到时抛出而不是静默跳过。</summary>
    /// <exception cref="InvalidOperationException">两条解析路径都拿不到含标志物的目录。</exception>
    public static string Locate() => Cached.Value;

    private static string Resolve()
        => FromAssemblyMetadata()
           ?? FromDirectoryWalk()
           ?? throw new InvalidOperationException(
               $"无法定位仓库根（标志物 {MarkerFile}）。源码扫描类门禁在这种状态下什么也扫不到，"
               + "因此这里刻意抛出而不是跳过 —— 静默通过会让「门禁没跑」伪装成「门禁通过」。"
               + $"已尝试：程序集元数据 {MetadataKey}，以及从 {AppContext.BaseDirectory} 逐级向上查找。");

    /// <summary>
    /// 编译期注入的路径。
    /// </summary>
    /// <remarks>
    /// 主路径。与运行时的输出目录位置无关，所以 <c>BaseOutputPath</c> 重定向、
    /// 拷贝产物到别处运行都不会让门禁退化。仍要校验标志物存在：跨机器搬运编译产物时
    /// 这个路径会指向一个不存在的目录，那种情况应当落到下一条路径而不是直接采信。
    /// </remarks>
    private static string? FromAssemblyMetadata()
    {
        var value = typeof(RepoRoot).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;

        if (string.IsNullOrWhiteSpace(value))
            return null;

        var full = Path.GetFullPath(value);
        return File.Exists(Path.Combine(full, MarkerFile)) ? full : null;
    }

    /// <summary>从运行目录逐级向上找标志物 —— 兜底路径，保留原有行为。</summary>
    private static string? FromDirectoryWalk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, MarkerFile)))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}
