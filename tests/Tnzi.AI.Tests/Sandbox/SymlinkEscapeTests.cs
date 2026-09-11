using System.Security;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 沙箱的越界判定必须解析符号链接。
/// </summary>
/// <remarks>
/// <para>
/// ★ 判定原本是"<see cref="Path.GetFullPath(string)"/> 后必须以工作区目录开头"，而
/// <c>GetFullPath</c> 只做<b>字面</b>规范化（消掉 <c>..</c> 与 <c>.</c>），不跟随符号链接。
/// 于是 agent 在工作区里放一个链接：
/// </para>
/// <code>ln -s /etc /mnt/workspace/x</code>
/// <para>
/// 之后 <c>read_file("/mnt/workspace/x/passwd")</c> 与 <c>ls</c> 就落到宿主的 <c>/etc</c> 上，
/// 而每一层校验都认为它待在界内 —— 字面上它确实是。<c>read_file</c> / <c>ls</c> 正是按
/// "被围住"设计的（三层防护都在），所以这一条穿的是设计意图本身。
/// </para>
/// <para>
/// Windows 上创建符号链接要求开发者模式或管理员权限；拿不到就跳过（<see cref="SymlinkFactAttribute"/>），
/// 但解析器本身的语义在下面用不需要链接的用例锁住。
/// </para>
/// </remarks>
public class SymlinkEscapeTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;
    private readonly Guid _threadId = Guid.Parse("00000000-0000-0000-0000-000000000009");

    public SymlinkEscapeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"tnzi-symlink-{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"tnzi-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "host secret");
    }

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_outside);
        GC.SuppressFinalize(this);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    // -------------------------------------------------------------------------
    // 解析器语义（不需要符号链接）
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_PlainPath_IsJustTheFullPath()
    {
        var file = Path.Combine(_root, "plain.txt");
        File.WriteAllText(file, "x");

        RealPathResolver.Resolve(file).ShouldBe(Path.GetFullPath(file));
    }

    [Fact]
    public void Resolve_NonExistentTail_KeepsTheTailVerbatim()
    {
        // 写新文件的常见情形：尾段还不存在，也就还不可能是链接，但它的祖先已被解析。
        var target = Path.Combine(_root, "sub", "not-created-yet.txt");

        RealPathResolver.Resolve(target).ShouldBe(Path.GetFullPath(target));
    }

    [Fact]
    public void Resolve_DotSegments_AreStillNormalised()
    {
        var messy = Path.Combine(_root, "a", "..", "b.txt");

        RealPathResolver.Resolve(messy).ShouldBe(Path.GetFullPath(Path.Combine(_root, "b.txt")));
    }

    // -------------------------------------------------------------------------
    // 真实符号链接
    // -------------------------------------------------------------------------

    [SymlinkFact]
    public void ToPhysical_ThroughASymlinkedDirectory_IsRefused()
    {
        var translator = new VirtualPathTranslator(_root);
        translator.EnsureThreadDirectories(_threadId);

        var workspace = Path.Combine(translator.GetThreadDirectory(_threadId), "workspace");
        Directory.CreateSymbolicLink(Path.Combine(workspace, "x"), _outside);

        // 末端 secret.txt 本身不是链接 —— 是它的父目录是。只解析末端的实现在这里照样放行。
        Should.Throw<SecurityException>(() => translator.ToPhysical("/mnt/workspace/x/secret.txt", _threadId));
    }

    [SymlinkFact]
    public void ToPhysical_ThroughASymlinkedFile_IsRefused()
    {
        var translator = new VirtualPathTranslator(_root);
        translator.EnsureThreadDirectories(_threadId);

        var workspace = Path.Combine(translator.GetThreadDirectory(_threadId), "workspace");
        File.CreateSymbolicLink(Path.Combine(workspace, "leak.txt"), Path.Combine(_outside, "secret.txt"));

        Should.Throw<SecurityException>(() => translator.ToPhysical("/mnt/workspace/leak.txt", _threadId));
    }

    [SymlinkFact]
    public void ToPhysical_SymlinkStayingInsideTheThreadDirectory_IsAllowed()
    {
        // 对照组：解析不能把界内的链接也拒掉。
        var translator = new VirtualPathTranslator(_root);
        translator.EnsureThreadDirectories(_threadId);

        var threadDir = translator.GetThreadDirectory(_threadId);
        var real = Path.Combine(threadDir, "outputs", "report.md");
        File.WriteAllText(real, "ok");
        File.CreateSymbolicLink(Path.Combine(threadDir, "workspace", "report.md"), real);

        var resolved = translator.ToPhysical("/mnt/workspace/report.md", _threadId);

        resolved.ShouldBe(Path.GetFullPath(real));
    }

    [SymlinkFact]
    public async Task LocalSandbox_ReadingThroughASymlink_IsRefused()
    {
        var workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(workspace);
        Directory.CreateSymbolicLink(Path.Combine(workspace, "x"), _outside);

        await using var sandbox = new LocalSandbox(
            id: "local-test",
            workspacePath: workspace,
            commandTimeout: TimeSpan.FromSeconds(5),
            maxOutputSize: 1024);

        await Should.ThrowAsync<SecurityException>(
            () => sandbox.ReadFileAsync(Path.Combine(workspace, "x", "secret.txt")));
    }

    [SymlinkFact]
    public async Task LocalSandbox_ListingThroughASymlink_IsRefused()
    {
        var workspace = Path.Combine(_root, "ws2");
        Directory.CreateDirectory(workspace);
        Directory.CreateSymbolicLink(Path.Combine(workspace, "x"), _outside);

        await using var sandbox = new LocalSandbox(
            id: "local-test",
            workspacePath: workspace,
            commandTimeout: TimeSpan.FromSeconds(5),
            maxOutputSize: 1024);

        await Should.ThrowAsync<SecurityException>(
            () => sandbox.ListDirectoryAsync(Path.Combine(workspace, "x")));
    }
}

/// <summary>
/// 判断这台机器能不能创建符号链接（Windows 上需要开发者模式或管理员权限）。
/// </summary>
public static class SymlinkCapability
{
    private static readonly Lazy<string?> Reason = new(Probe);

    /// <summary>不能创建符号链接时给出跳过理由，能创建时为 null。</summary>
    public static string? SkipReason => Reason.Value;

    private static string? Probe()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tnzi-symlink-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "target");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(Path.Combine(dir, "link"), target);
            return null;
        }
        catch (Exception ex)
        {
            return $"This machine cannot create symbolic links ({ex.GetType().Name}); " +
                   "on Windows that needs Developer Mode or elevation.";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}

/// <summary>
/// 需要真实符号链接的用例。
/// </summary>
/// <remarks>
/// 与 CLI smoke gate 同一形状：xUnit 2.x 没有运行期 skip，所以在发现期设 <c>Skip</c>。
/// 这里也正合适 —— 能不能建链接在整轮运行里是固定的。
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SymlinkFactAttribute : FactAttribute
{
    public SymlinkFactAttribute() => Skip = SymlinkCapability.SkipReason;
}
