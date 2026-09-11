namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="IFileReadAccessProbe"/> 的默认实现：取出记录，交给
/// <see cref="IFileAccessAuthorizer"/> 判定。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>走的是 <see cref="IFileAccessAuthorizer.CanMintAccessTokenAsync"/> 而不是
/// <c>CanReadAsync</c>。</b>两者是同一条读取判据，差别只在认不认<b>请求级</b>凭据
/// （URL 签名令牌、分享链接授予）。本探针服务的是<b>写入</b>路径 —— 调用方要把这个
/// 文件 id 写进自己的业务记录，而那条记录随后按它自己的规则对一群人可见。
/// 认了请求级凭据的话，一条限次数、会过期的分享链接就能被换成一条永久引用，
/// 而引用一旦落库，原来那份凭据的全部约束都不再适用。
/// 这与「签发访问令牌不认签名令牌」是同一个理由的同一次应用。
/// </para>
/// <para>
/// ★ 记录不存在（或已被清理）时答 <see langword="false"/>：引用一个不存在的 id
/// 不是一次合法的写入。实体 ID 是**顺序 GUID**，可预测性远高于随机 GUID，
/// 所以「猜一个 id 写进去等以后有人上传到这个位置」不是纯理论的顾虑。
/// </para>
/// </remarks>
public class FileReadAccessProbe : IFileReadAccessProbe
{
    private readonly IReadOnlyRepository<FileRecord, Guid> _records;
    private readonly IFileAccessAuthorizer _authorizer;

    public FileReadAccessProbe(
        IReadOnlyRepository<FileRecord, Guid> records,
        IFileAccessAuthorizer authorizer)
    {
        _records = Check.NotNull(records);
        _authorizer = Check.NotNull(authorizer);
    }

    /// <inheritdoc />
    public async Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (fileId == Guid.Empty)
            return false;

        var record = await _records.FindAsync(fileId, cancellationToken);
        if (record == null)
            return false;

        return await _authorizer.CanMintAccessTokenAsync(record, cancellationToken);
    }
}
