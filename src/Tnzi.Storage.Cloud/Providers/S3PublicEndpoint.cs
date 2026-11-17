using Amazon.Runtime.Internal.Auth;

namespace Tnzi.Storage.Cloud.Providers;

/// <summary>
/// 预签名 URL 的公开端点，<see cref="S3Storage"/> 与 <see cref="R2Storage"/> 共用。
/// </summary>
/// <remarks>
/// <para>
/// 应用与浏览器经不同地址到达同一个存储的部署（应用在内网经 <c>http://10.0.0.5:3900</c>，浏览器经 <c>https://s3.example.com</c>）
/// 要把预签名 URL 交给浏览器，就得对公开主机签：SigV4 签名覆盖 Host，签完再把主机改写掉，存储验签就对不上。
/// 所以 provider 另建一个只用来签名的客户端，端点是公开地址，其余操作仍走私网客户端。签名是本地的密码学运算，
/// 这个客户端从不发请求。
/// </para>
/// <para>
/// ★ <b>签名区域钉成私网那一路实际用的区域，而不是让 SDK 从公开主机名里再推一次。</b>没有显式签名区域时，SDK
/// 从 <c>ServiceURL</c> 的主机名推区域：<c>10.0.0.5</c> 推不出、落到 <c>us-east-1</c>，<c>s3.eu-west-1.example.com</c>
/// 却会推成 <c>eu-west-1</c>。两路区域不同，同一个存储就会接受内网的签名而拒绝公开链接的签名。
/// 这里直接问 SDK 私网配置签的是哪个区域（与它签名时走的是同一个函数），再显式设给公开那一路。
/// </para>
/// </remarks>
internal static class S3PublicEndpoint
{
    /// <summary>
    /// 公开端点那一路签名客户端的配置：路径样式、scheme 取公开地址自己的、签名区域与 <paramref name="privateConfig"/> 相同。
    /// </summary>
    /// <param name="privateConfig">私网客户端的配置，已设好端点与签名区域。</param>
    /// <param name="publicServiceUrl">浏览器访问存储的地址，绝对的 http/https URL。</param>
    /// <exception cref="ArgumentException"><paramref name="publicServiceUrl"/> 不是绝对的 http/https URL。</exception>
    public static AmazonS3Config ConfigFor(AmazonS3Config privateConfig, string publicServiceUrl)
    {
        Check.NotNull(privateConfig);

        // 选项验证器在启动时已拦过；这里再拦一次是给直接 new provider 的调用方。
        if (!Uri.TryCreate(publicServiceUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"PublicServiceUrl must be an absolute http or https URL, but was '{publicServiceUrl}'.",
                nameof(publicServiceUrl));
        }

        return new AmazonS3Config
        {
            ServiceURL = publicServiceUrl,
            // 公开地址前面是一个主机，桶名不能被提升成它的子域名。
            ForcePathStyle = true,
            UseHttp = uri.Scheme == Uri.UriSchemeHttp,
            AuthenticationRegion = AWS4Signer.DetermineSigningRegion(privateConfig, "s3", alternateEndpoint: null, request: null)
        };
    }

    /// <summary>
    /// 公开端点那一路预签名请求的 scheme。显式给出是因为预签名请求的 <c>Protocol</c> 会盖过端点自带的 scheme，
    /// 而私网那一路的 scheme（<c>UseHttps</c>）与公开地址的往往不同。
    /// </summary>
    public static Protocol ProtocolOf(AmazonS3Config publicConfig) => publicConfig.UseHttp ? Protocol.HTTP : Protocol.HTTPS;
}
