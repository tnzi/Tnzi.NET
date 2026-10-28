namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 容器用户能否写进挂载源。原生 Linux 的 bind mount 保留宿主属主：容器以 1000:1000 运行，挂载源却属于本进程，
/// 两者 uid 不同时容器里一个文件都写不了，而只有 Docker Desktop 或恰好以 uid 1000 运行的进程才不会撞上。
/// </summary>
public class DockerBindSourcesTests
{
    private static readonly ContainerIdentity DefaultSandboxUser = new(1000, 1000);

    /// <summary>
    /// 技能目录在容器里只读，两个入口都是：<c>/mnt/skills</c>，以及可写的线程根下面的 <c>/workspace/skills</c>
    /// （叠一层只读挂载盖住它）。三个工作目录照常可写。
    /// </summary>
    /// <remarks>
    /// 容器能写进技能目录的话就能在里面放链接；技能副本归框架所有，文档写明容器对它只读。
    /// </remarks>
    [Fact]
    public void BuildBinds_MountsSkillsReadOnlyEverywhere_AndTheWorkingDirectoriesWritable()
    {
        var thread = Path.Combine(Path.GetTempPath(), "sandbox-threads", "t1");
        string Source(string mount) => Path.Combine(thread, mount);

        var binds = DockerBindSources.BuildBinds(thread);

        binds.ShouldContain($"{thread}:/workspace");
        binds.ShouldContain($"{Source("workspace")}:/mnt/workspace");
        binds.ShouldContain($"{Source("uploads")}:/mnt/uploads");
        binds.ShouldContain($"{Source("outputs")}:/mnt/outputs");
        binds.ShouldContain($"{Source("skills")}:/mnt/skills:ro");
        binds.ShouldContain($"{Source("skills")}:/workspace/skills:ro");

        // 技能目录没有任何一个可写的挂载
        binds.Where(b => b.StartsWith(Source("skills") + ":", StringComparison.Ordinal))
            .ShouldAllBe(b => b.EndsWith(":ro", StringComparison.Ordinal));
        binds.Count(b => b.EndsWith(":ro", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public void RootProcess_HandsTheWritableSourcesToTheContainerUser()
    {
        DockerBindSources.DecideOwnership(isLinux: true, processUid: 0, DefaultSandboxUser)
            .ShouldBe(BindOwnershipAction.HandOverToContainerUser);
    }

    [Fact]
    public void NonRootProcessWithAnotherUid_CannotChownAndSaysSo()
    {
        // The .NET container images run as uid 1654; a systemd service has its own user.
        DockerBindSources.DecideOwnership(isLinux: true, processUid: 1654, DefaultSandboxUser)
            .ShouldBe(BindOwnershipAction.WarnNotWritable);
    }

    [Theory]
    [InlineData(true, 1000u)]   // same uid: the sources are already the container user's
    [InlineData(false, 0u)]     // Docker Desktop: file sharing does not keep host ownership
    [InlineData(false, 1654u)]
    public void NothingToDo(bool isLinux, uint processUid)
    {
        DockerBindSources.DecideOwnership(isLinux, processUid, DefaultSandboxUser).ShouldBe(BindOwnershipAction.None);
    }

    [Fact]
    public void NamedContainerUser_CannotBeResolvedOnTheHost_SoNothingIsChanged()
    {
        DockerBindSources.DecideOwnership(isLinux: true, processUid: 0, DockerBindSources.ParseNumericIdentity("sandbox"))
            .ShouldBe(BindOwnershipAction.None);
    }

    [Theory]
    [InlineData("1000:1000", 1000u, 1000u)]
    [InlineData("1654", 1654u, null)]
    [InlineData(" 2000:3000 ", 2000u, 3000u)]
    public void ParseNumericIdentity_ReadsUidAndOptionalGid(string runAsUser, uint uid, uint? gid)
    {
        DockerBindSources.ParseNumericIdentity(runAsUser).ShouldBe(new ContainerIdentity(uid, gid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sandbox")]
    [InlineData("1000:sandbox")]
    [InlineData("1000:1000:1")]
    public void ParseNumericIdentity_NonNumeric_IsNull(string? runAsUser)
    {
        DockerBindSources.ParseNumericIdentity(runAsUser).ShouldBeNull();
    }

    /// <summary>
    /// 线程根不交给容器用户：根下是本进程写的东西（技能接线哨兵）与四个挂载源本身。容器能在根下改名、删除时，
    /// 就能把它们换成链接，而本进程随后会跟进去（写哨兵、chown、守护进程按链接目标建 bind mount）。
    /// </summary>
    [Fact]
    public void WritableSources_AreTheThreeWorkDirectories_NotTheThreadRootOrSkills()
    {
        var root = Path.Combine(Path.GetTempPath(), "thread");

        DockerBindSources.WritableSources(root).ShouldBe(
        [
            Path.Combine(root, "workspace"),
            Path.Combine(root, "uploads"),
            Path.Combine(root, "outputs")
        ]);
    }

    /// <summary>
    /// 挂载源若是链接，Docker 守护进程按宿主上的链接目标建 bind mount：容器在上一次运行里把 <c>workspace</c> 换成指向
    /// 宿主任意目录（应用目录、别的线程的目录）的链接，下一次建容器就把那个目录可写地挂了进来。
    /// 建容器前把链接换回真目录，链接目标原样不动。
    /// </summary>
    [SymlinkFact]
    public void EnsureExist_AMountSourceThatIsALink_IsReplacedByARealDirectory_TargetUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-dbs-" + Guid.NewGuid().ToString("N"));
        var thread = Path.Combine(root, "thread");
        var hostDir = Path.Combine(root, "host");
        try
        {
            Directory.CreateDirectory(thread);
            Directory.CreateDirectory(hostDir);
            File.WriteAllText(Path.Combine(hostDir, "keep.txt"), "host");
            Directory.CreateSymbolicLink(Path.Combine(thread, "workspace"), hostDir);
            File.CreateSymbolicLink(Path.Combine(thread, "outputs"), Path.Combine(hostDir, "keep.txt"));

            DockerBindSources.EnsureExist(thread);

            foreach (var mount in new[] { "workspace", "uploads", "outputs", "skills" })
            {
                var source = Path.Combine(thread, mount);
                new DirectoryInfo(source).LinkTarget.ShouldBeNull();
                Directory.Exists(source).ShouldBeTrue();
            }

            File.ReadAllText(Path.Combine(hostDir, "keep.txt")).ShouldBe("host");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [SymlinkFact]
    public void HandOver_RefusesALink()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-dbs-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "host"));
            var link = Path.Combine(root, "outputs");
            Directory.CreateSymbolicLink(link, Path.Combine(root, "host"));

            Should.Throw<IOException>(() => DockerBindSources.HandOver(link, DefaultSandboxUser));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
