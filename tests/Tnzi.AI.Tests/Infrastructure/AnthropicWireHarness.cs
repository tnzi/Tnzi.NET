using System.Net;
using System.Text;
using Anthropic;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 把真实的 Anthropic SDK 适配器（<c>AsIChatClient</c>）接到一个截获出站请求体的
/// <see cref="HttpMessageHandler"/> 上，供线路级断言使用：框架写入的 thinking / cache_control
/// 是否真的出现在出站 JSON 里。此前两处注入都写在 SDK 从不读取的 <c>AdditionalProperties</c> 键上，
/// 单元测试 mock 掉内层 client 后只断言那个没人读的键，于是「从未上线」六个版本无人察觉。
/// </summary>
internal static class AnthropicWireHarness
{
    public const string Model = "claude-sonnet-4-5";

    public static (IChatClient client, CapturingHandler handler) CreateClient()
    {
        var handler = new CapturingHandler();
        var anthropic = new AnthropicClient
        {
            ApiKey = "sk-ant-test",
            HttpClient = new HttpClient(handler)
        };
        IChatClient chatClient = new AnthropicThinkingChatClient(anthropic.AsIChatClient(Model));
        return (chatClient, handler);
    }

    public sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            const string responseJson = """
                {"id":"msg_test","type":"message","role":"assistant","model":"claude-sonnet-4-5",
                 "content":[{"type":"text","text":"ok"}],
                 "stop_reason":"end_turn","stop_sequence":null,
                 "usage":{"input_tokens":1,"output_tokens":1}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
