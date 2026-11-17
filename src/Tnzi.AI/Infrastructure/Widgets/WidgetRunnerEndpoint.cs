namespace Tnzi.AI.Infrastructure.Widgets;

/// <summary>
/// Serves the widget runner: the static page a client loads in an iframe to show an inline widget
/// (<c>show_widget</c>). The page receives the widget's document over <c>postMessage</c> and shows
/// it in a sandboxed inner frame, relaying messages in the shape of the MCP Apps host protocol.
/// </summary>
/// <remarks>
/// <para>
/// The page carries its own policy instead of the host page's: a widget is HTML with inline
/// scripts, which a chat page under <c>script-src 'self'</c> (a desktop shell, a strict web build)
/// would block in a srcdoc frame of its own, since a srcdoc frame inherits its embedder's policy.
/// Here the response's policy applies to the runner and to the widget it shows.
/// </para>
/// <para>
/// <c>sandbox allow-scripts allow-forms</c> gives both an opaque origin (no cookies, storage or
/// token of any site), and the fetch directives allow no network at all: images, fonts and media
/// only from <c>data:</c> / <c>blob:</c>. <c>X-Frame-Options</c> set by the security-headers
/// middleware is removed, since being framed is this page's purpose; who may frame it is
/// <c>frame-ancestors</c>.
/// </para>
/// <para>
/// <c>allow-forms</c> is there only so a widget's <c>&lt;form&gt;</c> fires <c>submit</c> (and
/// Enter in a field submits it): without it the browser drops the submission before the event, and
/// widget authors write ordinary forms. It grants no way out. A form submission is a navigation,
/// and <c>form-action 'none'</c> blocks every action URL, for this page and for the widget's srcdoc
/// frame, which inherits the policy; a <c>target</c> of <c>_blank</c>, <c>_top</c> or
/// <c>_parent</c> stays blocked by the absence of <c>allow-popups</c> and
/// <c>allow-top-navigation</c> and by the sandbox's rule that a frame navigates only itself and
/// its descendants; and the origin stays opaque with <c>connect-src 'none'</c>. The client's bridge
/// also cancels every submission the widget did not, so a form never starts to navigate at all.
/// </para>
/// </remarks>
public static class WidgetRunnerEndpoint
{
    private const string ResourceName = "Tnzi.AI.Infrastructure.Widgets.widget-runner.html";

    private static readonly Lazy<string> Page = new(LoadPage);

    /// <summary>The runner page's HTML.</summary>
    public static string Html => Page.Value;

    /// <summary>The runner page's Content-Security-Policy, with the given <c>frame-ancestors</c> source list.</summary>
    public static string BuildContentSecurityPolicy(string frameAncestors)
    {
        Check.NotNullOrWhiteSpace(frameAncestors);
        return "sandbox allow-scripts allow-forms; " +
               "default-src 'none'; " +
               "script-src 'unsafe-inline'; " +
               "style-src 'unsafe-inline'; " +
               "img-src data: blob:; " +
               "font-src data:; " +
               "media-src data: blob:; " +
               "connect-src 'none'; " +
               "worker-src 'none'; " +
               "form-action 'none'; " +
               "base-uri 'none'; " +
               $"frame-ancestors {frameAncestors}";
    }

    /// <summary>Maps <c>GET {options.RunnerPath}</c> to the runner page. Anonymous: the page holds nothing.</summary>
    public static IEndpointConventionBuilder MapWidgetRunner(this IEndpointRouteBuilder endpoints, WidgetOptions options)
    {
        Check.NotNull(endpoints);
        Check.NotNull(options);

        var policy = BuildContentSecurityPolicy(options.FrameAncestors);
        return endpoints
            .MapGet(options.RunnerPath, (HttpContext httpContext) => WriteAsync(httpContext, policy))
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static Task WriteAsync(HttpContext httpContext, string policy)
    {
        var headers = httpContext.Response.Headers;
        headers.Remove("X-Frame-Options");
        headers.ContentSecurityPolicy = policy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), usb=(), payment=(), clipboard-read=()";
        // Short-lived: a new framework version may change the page, and it is tiny.
        headers.CacheControl = "public, max-age=300";
        httpContext.Response.ContentType = "text/html; charset=utf-8";
        return httpContext.Response.WriteAsync(Html, httpContext.RequestAborted);
    }

    private static string LoadPage()
    {
        using var stream = typeof(WidgetRunnerEndpoint).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from {typeof(WidgetRunnerEndpoint).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
