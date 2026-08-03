namespace Tnzi.Storage.Tests;

/// <summary>
/// 测试用的签名器：可预测、不依赖配置。
///
/// 真实的 <see cref="FileUrlSigner"/> 要从 <c>IConfiguration</c> 解析密钥，而绝大多数
/// 既有用例只是路过它（构造 <c>FileStorageService</c> 需要一个实例而已）。签名算法本身
/// 由 <c>FileUrlSignerTests</c> 直接针对真实实现覆盖。
/// </summary>
public sealed class TestFileUrlSigner : IFileUrlSigner
{
    /// <summary>被认作有效的令牌。默认只认这一个常量，任何其它值都判无效。</summary>
    public const string ValidToken = "test-signature";

    private readonly HashSet<Guid> _signable;

    /// <param name="signableFileIds">
    /// 允许通过校验的文件；留空表示任何文件配上 <see cref="ValidToken"/> 都算有效。
    /// </param>
    public TestFileUrlSigner(params Guid[] signableFileIds)
    {
        _signable = signableFileIds.ToHashSet();
    }

    public string Sign(Guid fileId, DateTimeOffset expiresAt, Guid? userId) => ValidToken;

    public bool TryValidate(Guid fileId, string? token, out Guid? userId)
    {
        userId = null;
        if (token != ValidToken)
            return false;

        return _signable.Count == 0 || _signable.Contains(fileId);
    }
}
