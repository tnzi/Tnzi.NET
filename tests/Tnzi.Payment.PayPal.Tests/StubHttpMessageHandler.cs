namespace Tnzi.Payment.PayPal.Tests;

/// <summary>
/// 按「方法 + 路径」派发的 HTTP 桩，同时录下发出去的请求体供断言。
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<RecordedRequest, HttpResponseMessage>> _routes = new();

    public List<RecordedRequest> Requests { get; } = [];

    public void OnPost(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Post, path)] = responder;
    public void OnGet(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Get, path)] = responder;
    public void OnDelete(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Delete, path)] = responder;

    public JsonElement LastBodyFor(HttpMethod method, string path)
    {
        var request = Requests.Last(r => r.Method == method && r.Path == path);
        request.Body.ShouldNotBeNull($"No JSON body recorded for {method} {path}.");
        return request.Body.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        JsonElement? body = null;

        if (request.Content != null)
        {
            var raw = await request.Content.ReadAsStringAsync(cancellationToken);
            // 换令牌那一步是表单不是 JSON，解析失败不该让用例炸掉
            if (raw.StartsWith('{'))
                body = JsonDocument.Parse(raw).RootElement.Clone();
        }

        var recorded = new RecordedRequest(
            request.Method,
            path,
            body,
            request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.FirstOrDefault() : null);

        Requests.Add(recorded);

        return _routes.TryGetValue(Key(request.Method, path), out var responder)
            ? responder(recorded)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Key(HttpMethod method, string path) => $"{method}:{path}";
}

internal sealed record RecordedRequest(HttpMethod Method, string Path, JsonElement? Body, string? IdempotencyKey);
