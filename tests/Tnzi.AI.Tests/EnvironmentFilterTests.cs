using System.Diagnostics;
using Tnzi.AI.Coder.Options;

namespace Tnzi.AI.Tests;

/// <summary>
/// EnvironmentFilter + SandboxOptions 安全过滤测试
/// </summary>
public class EnvironmentFilterTests
{
    [Fact]
    public void ApplyFilter_Whitelist_OnlyAllowsWhitelisted()
    {
        var psi = new ProcessStartInfo();
        // ProcessStartInfo.Environment 默认包含当前进程环境变量
        psi.Environment["PATH"].ShouldNotBeNull();

        var sandbox = new SandboxOptions
        {
            EnvironmentWhitelist = ["PATH", "HOME"],
            EnvironmentBlacklist = []
        };

        EnvironmentFilter.ApplyEnvironmentFilter(psi, sandbox);

        // 白名单模式：只有 PATH 和 HOME 被保留
        // PATH 应该存在（当前进程肯定有 PATH）
        psi.Environment.ContainsKey("PATH").ShouldBeTrue();
        // 非白名单的随机变量不应存在
        psi.Environment.Count.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void ApplyFilter_Blacklist_RemovesMatchingKeys()
    {
        // EnvironmentFilter 遍历 Environment.GetEnvironmentVariables()（进程级），
        // 所以需要在进程级设置测试变量
        var testKey = "TNZI_TEST_API_KEY";
        Environment.SetEnvironmentVariable(testKey, "secret123");
        try
        {
            var psi = new ProcessStartInfo();
            // psi.Environment 自动包含进程环境变量

            var sandbox = new SandboxOptions
            {
                EnvironmentWhitelist = [],
                EnvironmentBlacklist = ["API_KEY"]
            };

            EnvironmentFilter.ApplyEnvironmentFilter(psi, sandbox);

            // 后缀匹配: TNZI_TEST_API_KEY 应被移除
            psi.Environment.ContainsKey(testKey).ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void ApplyFilter_EmptyBothLists_NoChange()
    {
        var psi = new ProcessStartInfo();
        var originalCount = psi.Environment.Count;

        var sandbox = new SandboxOptions
        {
            EnvironmentWhitelist = [],
            EnvironmentBlacklist = []
        };

        EnvironmentFilter.ApplyEnvironmentFilter(psi, sandbox);

        // 两个列表都为空，不做任何修改
        psi.Environment.Count.ShouldBe(originalCount);
    }

    [Fact]
    public void ApplyFilter_BlacklistCaseInsensitive()
    {
        var testKey = "tnzi_test_secret_key";
        Environment.SetEnvironmentVariable(testKey, "sk-test");
        try
        {
            var psi = new ProcessStartInfo();

            var sandbox = new SandboxOptions
            {
                EnvironmentWhitelist = [],
                EnvironmentBlacklist = ["SECRET_KEY"]
            };

            EnvironmentFilter.ApplyEnvironmentFilter(psi, sandbox);

            // 大小写不敏感后缀匹配
            psi.Environment.ContainsKey(testKey).ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }
}
