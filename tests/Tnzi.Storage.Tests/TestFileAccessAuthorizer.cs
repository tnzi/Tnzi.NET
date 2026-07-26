namespace Tnzi.Storage.Tests;

/// <summary>
/// 测试用的文件访问策略,行为由构造参数固定,不看当前用户也不查权限。
///
/// 绝大多数既有用例关心的是"存储逻辑本身对不对",不是"谁有权访问",所以默认
/// <see cref="AllowAll"/> 全放行,让它们保持原语义。授权行为本身由
/// <c>FileAccessAuthorizationTests</c> 针对真实实现 <c>FileAccessAuthorizer</c> 单独覆盖。
/// </summary>
public sealed class TestFileAccessAuthorizer : IFileAccessAuthorizer
{
    private readonly bool _canRead;
    private readonly bool _canWrite;

    public TestFileAccessAuthorizer(bool canRead = true, bool canWrite = true)
    {
        _canRead = canRead;
        _canWrite = canWrite;
    }

    /// <summary>读写全放行,用于与访问控制无关的存储逻辑用例。</summary>
    public static TestFileAccessAuthorizer AllowAll() => new(canRead: true, canWrite: true);

    /// <summary>只读,用于验证变更路径确实被挡住。</summary>
    public static TestFileAccessAuthorizer ReadOnly() => new(canRead: true, canWrite: false);

    /// <summary>全拒,用于验证读路径确实被挡住。</summary>
    public static TestFileAccessAuthorizer DenyAll() => new(canRead: false, canWrite: false);

    public Task<bool> CanReadAsync(FileRecord record, CancellationToken cancellationToken = default)
        => Task.FromResult(_canRead);

    public Task<bool> CanWriteAsync(FileRecord record, CancellationToken cancellationToken = default)
        => Task.FromResult(_canWrite);
}
