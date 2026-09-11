using System.Diagnostics;
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
/// <c>Tnzi.Tests</c> 的命名空间约定同理。
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

    /// <summary>读取仓库内某个路径的文本内容，供做源码文本断言的测试使用。</summary>
    /// <param name="relativePath">相对仓库根的路径，用 <c>/</c> 分隔（跨平台）。</param>
    /// <remarks>
    /// <para>
    /// 存在的理由是一次实测到的 CI 失败。Channels 那批测试用文本断言检查源码本身
    /// （"模块里确实注册了 Slack 适配器"），此前它们把路径写死成
    /// <c>"D:/dev/Repo/src/..."</c>：在作者本机通过，在 Linux runner 上那个绝对路径
    /// 被当成相对路径拼在 <c>bin/Debug/net10.0/</c> 后面，必然 DirectoryNotFoundException。
    /// </para>
    /// <para>
    /// 它一直没被发现，是因为 <c>backend-quality.yml</c> 的 full-suite job 更早一步就挂了
    /// （某消费应用的 NuGet.config 里有 Windows 本地 NuGet 源,runner 上不存在），
    /// 这批测试从未真正在 CI 上跑过。2026-08-14 该应用迁出、restore 通过后才第一次暴露。
    /// </para>
    /// <para>
    /// 读不到时抛 <see cref="FileNotFoundException"/> 并带上解析后的绝对路径 ——
    /// 与 <see cref="Locate"/> 同一个理由：让「文件找不到」与「断言通过」不可混淆。
    /// </para>
    /// </remarks>
    /// <exception cref="FileNotFoundException">目标文件不存在。</exception>
    /// <remarks>
    /// <para>
    /// ★★ <strong>行尾统一归一化成 <c>\n</c>。</strong>本方法的全部用途是「对仓库里的源码做文本断言」，
    /// 而<b>行尾从来不是这些断言要检查的东西</b> —— 它只会让同一条断言在 Linux 上通过、
    /// 在 Windows 检出上失败。
    /// </para>
    /// <para>
    /// ★ 这不是假想：<c>IconManifestTests</c> 的清单正则是
    /// <c>^\s{2}'(...)',$</c> 配 <c>RegexOptions.Multiline</c>，而 .NET 的 <c>$</c> 匹配的是
    /// <c>\n</c> <b>之前</b>的位置 —— CRLF 文件里 <c>,</c> 后面还有一个 <c>\r</c>，于是
    /// <b>一个都匹配不上</b>，清单被解析成空集合。同一个根因让 4 条架构门禁
    /// 加 1 条前端测试在 Windows 上恒红、在 CI（Linux 检出为 LF）上恒绿 ——
    /// 那比单纯的失败更糟：<b>本地跑测试这件事从此不再传递信息</b>，
    /// 真实的红被淹没在这几条常驻红里（本轮实测到自己差点把它误判成新引入的回归）。
    /// </para>
    /// <para>
    /// 仓库没有 <c>.gitattributes</c>，Windows 检出即 CRLF；与其让每一条源码文本断言各自记得
    /// 写 <c>\r?</c>，不如在唯一的入口处消除这个差异。真要断言文件的行尾，
    /// 直接用 <see cref="File.ReadAllText(string)"/>，不要走这里。
    /// </para>
    /// </remarks>
    public static string ReadText(string relativePath)
    {
        var full = Path.Combine(Locate(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            throw new FileNotFoundException(
                $"源码文本断言找不到目标文件：{relativePath}（解析为 {full}）。", full);
        }

        // 先折 CRLF 再折孤立的 CR：两步顺序反过来会把 CRLF 变成两个 \n。
        return File.ReadAllText(full).Replace("\r\n", "\n").Replace('\r', '\n');
    }

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
    /// 主路径。按 <see cref="MetadataSources"/> 的顺序问一圈，第一个指向真实仓库根的胜出。
    /// 与运行时的输出目录位置无关，所以 <c>BaseOutputPath</c> 重定向、
    /// 拷贝产物到别处运行都不会让门禁退化。仍要校验标志物存在：跨机器搬运编译产物时
    /// 这个路径会指向一个不存在的目录，那种情况应当落到下一条路径而不是直接采信。
    /// </remarks>
    private static string? FromAssemblyMetadata()
    {
        var own = typeof(RepoRoot).Assembly;

        foreach (var assembly in MetadataSources())
        {
            var value = assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == MetadataKey)?.Value;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var full = Path.GetFullPath(value);
            if (!Directory.Exists(full))
                continue;

            // ★ 标志物只对<b>本程序集</b>钉进去的那条路径校验。那条路径是发布者机器上的
            // 绝对路径，跨机器搬运编译产物时会指向一个碰巧存在的无关目录，所以要认标志物。
            // 而调用方显式声明的路径是他自己仓库的根 —— 拿框架的 slnx 去校验它必然失败，
            // 那正是这个类型此前只有框架仓自己用得了的原因。
            if (assembly == own && !File.Exists(Path.Combine(full, MarkerFile)))
                continue;

            return full;
        }

        return null;
    }

    /// <summary>
    /// 读元数据的程序集，按优先级：调用方 → 入口 → 本程序集。
    /// </summary>
    /// <remarks>
    /// ★★ <b>先问调用方，是这个包能被框架之外的人用起来的全部原因。</b>
    /// 本类型原先只读 <c>typeof(RepoRoot).Assembly</c>，也就是本程序集在<b>发布者机器上</b>
    /// 编译时钉进去的那个路径 —— 消费方拿到 NuGet 包之后，那个目录在他机器上根本不存在，
    /// 于是退到逐级向上找 <c>Tnzi.NET.slnx</c>，而那个标志物他也没有，最后抛异常。
    /// 换句话说：一个只读自己的实现，注定只有框架仓自己用得了。
    /// <para>
    /// 消费方要用扫源码的门禁，在自己的测试项目里加一行即可：
    /// <code>
    /// &lt;ItemGroup&gt;
    ///   &lt;AssemblyMetadata Include="TnziRepoRoot" Value="$(MSBuildThisFileDirectory)..\.." /&gt;
    /// &lt;/ItemGroup&gt;
    /// </code>
    /// 声明了就采信（只校验目录存在）：那是<b>他的</b>仓库根，拿框架的 <c>Tnzi.NET.slnx</c>
    /// 去校验它必然失败。逐级向上那条路径找的仍是框架标志物，消费方走不通，也不需要走。
    /// </para>
    /// </remarks>
    private static IEnumerable<Assembly> MetadataSources()
    {
        var seen = new HashSet<Assembly>();

        // 调用栈上第一个不是本程序集的，就是提问的那个测试程序集。
        foreach (var frame in new StackTrace(fNeedFileInfo: false).GetFrames())
        {
            var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
            if (assembly != null && assembly != typeof(RepoRoot).Assembly && seen.Add(assembly))
                yield return assembly;
        }

        var entry = Assembly.GetEntryAssembly();
        if (entry != null && seen.Add(entry)) yield return entry;

        if (seen.Add(typeof(RepoRoot).Assembly)) yield return typeof(RepoRoot).Assembly;
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
