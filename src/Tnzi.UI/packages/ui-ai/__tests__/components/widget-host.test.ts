/**
 * The host side of a widget frame: it answers only its own frame, hands the document over once the
 * runner is ready, and lets a widget do nothing beyond showing itself unless the product says so
 * (a link opens only as http(s); a message only with a handler and the user's yes; a tool only
 * when allow-listed).
 */
import { describe, expect, it, vi } from 'vitest';
import { createWidgetHost, widgetLinkUrl, widgetMessageText, type WidgetHostHandlers } from '../../src/components/widget/widget-host';
import { MCP_APPS_PROTOCOL_VERSION, type WidgetHostContext } from '../../src/components/widget/widget-document';

const CONTEXT: WidgetHostContext = { theme: 'light', styles: { variables: { '--color-text-primary': '#333' } }, displayMode: 'inline' };

interface Harness {
  readonly frame: { postMessage: ReturnType<typeof vi.fn> };
  readonly page: { open: ReturnType<typeof vi.fn> };
  /** A message from `source` (the frame by default). */
  readonly receive: (data: unknown, source?: unknown) => void;
  /** Everything posted to the frame. */
  readonly sent: () => Array<Record<string, any>>;
  /** The reply to request `id`, once it arrived. */
  readonly reply: (id: string | number) => Promise<Record<string, any>>;
  readonly host: ReturnType<typeof createWidgetHost>;
  readonly onSize: ReturnType<typeof vi.fn>;
  readonly onNavigated: ReturnType<typeof vi.fn>;
}

function harness(options: { handlers?: WidgetHostHandlers; confirm?: boolean } = {}): Harness {
  let listener: ((event: MessageEvent) => void) | null = null;
  const page = {
    open: vi.fn(),
    addEventListener: vi.fn((_: string, l: (event: MessageEvent) => void) => (listener = l)),
    removeEventListener: vi.fn(() => (listener = null)),
  };
  const frame = { postMessage: vi.fn() };
  const onSize = vi.fn();
  const onNavigated = vi.fn();
  const host = createWidgetHost({
    window: page as unknown as Window,
    frame: () => frame as unknown as Window,
    document: '<p>widget</p>',
    title: 'Usage',
    context: () => CONTEXT,
    onSize,
    onNavigated,
    confirmMessage: options.confirm === undefined ? undefined : async () => options.confirm as boolean,
    handlers: options.handlers,
  });
  const sent = () => frame.postMessage.mock.calls.map((call) => call[0] as Record<string, any>);
  return {
    frame,
    page,
    receive: (data, source = frame) => listener?.({ data, source } as MessageEvent),
    sent,
    reply: async (id) => {
      await vi.waitFor(() => expect(sent().some((m) => m.id === id)).toBe(true));
      return sent().find((m) => m.id === id)!;
    },
    host,
    onSize,
    onNavigated,
  };
}

const request = (id: number, method: string, params: object = {}) => ({ jsonrpc: '2.0', id, method, params });
const notification = (method: string, params: object = {}) => ({ jsonrpc: '2.0', method, params });

describe('the widget host', () => {
  it('hands the document over once the runner is ready', () => {
    const h = harness();
    h.receive(notification('ui/notifications/sandbox-proxy-ready'));
    expect(h.sent()).toEqual([
      { jsonrpc: '2.0', method: 'ui/notifications/sandbox-resource-ready', params: { html: '<p>widget</p>', title: 'Usage', colorScheme: 'light' } },
    ]);
  });

  it('reads nothing but its own frame', () => {
    const h = harness();
    h.receive(notification('ui/notifications/sandbox-proxy-ready'), { postMessage: vi.fn() });
    h.receive(notification('ui/notifications/size-changed', { height: 300 }), null);
    h.receive({ method: 'ui/notifications/sandbox-proxy-ready' });
    expect(h.sent()).toHaveLength(0);
    expect(h.onSize).not.toHaveBeenCalled();
  });

  it('answers ui/initialize with the host context, and sends the tool input once initialized', async () => {
    const h = harness();
    h.receive(request(1, 'ui/initialize', { protocolVersion: MCP_APPS_PROTOCOL_VERSION }));
    const reply = await h.reply(1);
    expect(reply.result.protocolVersion).toBe(MCP_APPS_PROTOCOL_VERSION);
    expect(reply.result.hostContext).toEqual(CONTEXT);
    expect(reply.result.hostCapabilities).toEqual({ openLinks: {} });

    h.receive(notification('ui/notifications/initialized'));
    h.receive(notification('ui/notifications/initialized'));
    const inputs = h.sent().filter((m) => m.method === 'ui/notifications/tool-input');
    expect(inputs).toEqual([{ jsonrpc: '2.0', method: 'ui/notifications/tool-input', params: { arguments: { title: 'Usage' } } }]);
  });

  it('follows the size the widget reports, ignoring nonsense', () => {
    const h = harness();
    h.receive(notification('ui/notifications/size-changed', { width: 320, height: 240.2 }));
    h.receive(notification('ui/notifications/size-changed', { height: -1 }));
    h.receive(notification('ui/notifications/size-changed', { height: 'tall' }));
    h.receive(notification('ui/notifications/size-changed', { height: 1e9 }));
    expect(h.onSize.mock.calls.map((c) => c[0])).toEqual([
      { width: 320, height: 241 },
      { width: null, height: 100_000 },
    ]);
  });

  it('opens http(s) links in a new tab without an opener, and nothing else', async () => {
    const h = harness();
    h.receive(request(1, 'ui/open-link', { url: 'https://example.com/a' }));
    expect((await h.reply(1)).result).toEqual({});
    expect(h.page.open).toHaveBeenCalledWith('https://example.com/a', '_blank', 'noopener,noreferrer');

    h.receive(request(2, 'ui/open-link', { url: 'javascript:alert(1)' }));
    expect((await h.reply(2)).error.code).toBe(-32602);
    expect(h.page.open).toHaveBeenCalledTimes(1);
  });

  it('opens no link the user did not just click', async () => {
    const openLink = vi.fn();
    const h = harness({ handlers: { openLink } });
    (h.page as unknown as { navigator: unknown }).navigator = { userActivation: { isActive: false } };
    h.receive(request(1, 'ui/open-link', { url: 'https://example.com/a' }));
    expect((await h.reply(1)).error.code).toBe(-32000);
    expect(openLink).not.toHaveBeenCalled();

    (h.page as unknown as { navigator: unknown }).navigator = { userActivation: { isActive: true } };
    h.receive(request(2, 'ui/open-link', { url: 'https://example.com/a' }));
    expect((await h.reply(2)).result).toEqual({});
    expect(openLink).toHaveBeenCalledTimes(1);
  });

  it('routes links to the product when it says so', async () => {
    const openLink = vi.fn();
    const h = harness({ handlers: { openLink } });
    h.receive(request(1, 'ui/open-link', { url: 'http://example.com/' }));
    await h.reply(1);
    expect(openLink).toHaveBeenCalledWith('http://example.com/');
    expect(h.page.open).not.toHaveBeenCalled();
  });

  it('sends no message without a handler, and none the user did not confirm', async () => {
    const sendMessage = vi.fn();
    const message = { role: 'user', content: [{ type: 'text', text: 'Set it to 40%' }] };

    const off = harness();
    off.receive(request(1, 'ui/message', message));
    expect((await off.reply(1)).error.code).toBe(-32601);

    const declined = harness({ handlers: { sendMessage }, confirm: false });
    declined.receive(request(1, 'ui/message', message));
    expect((await declined.reply(1)).error.code).toBe(-32000);

    const unasked = harness({ handlers: { sendMessage } });
    unasked.receive(request(1, 'ui/message', message));
    expect((await unasked.reply(1)).error).toBeDefined();
    expect(sendMessage).not.toHaveBeenCalled();

    const confirmed = harness({ handlers: { sendMessage }, confirm: true });
    confirmed.receive(request(1, 'ui/message', message));
    expect((await confirmed.reply(1)).result).toEqual({});
    expect(sendMessage).toHaveBeenCalledWith('Set it to 40%');
  });

  it('calls only allow-listed tools', async () => {
    const callTool = vi.fn(async () => ({ content: [{ type: 'text', text: 'ok' }] }));
    const h = harness({ handlers: { callTool, allowedTools: ['get_usage'] } });

    h.receive(request(1, 'tools/call', { name: 'delete_everything', arguments: {} }));
    expect((await h.reply(1)).error.code).toBe(-32601);

    h.receive(request(2, 'tools/call', { name: 'get_usage', arguments: { window: 'week' } }));
    expect((await h.reply(2)).result).toEqual({ content: [{ type: 'text', text: 'ok' }] });
    expect(callTool).toHaveBeenCalledTimes(1);
    expect(callTool).toHaveBeenCalledWith('get_usage', { window: 'week' });

    h.receive(request(3, 'ui/initialize'));
    expect((await h.reply(3)).result.hostCapabilities).toEqual({ openLinks: {}, serverTools: {} });
  });

  it('answers an unknown request with method not found', async () => {
    const h = harness();
    h.receive(request(7, 'ui/request-display-mode', { mode: 'pip' }));
    expect((await h.reply(7)).error.code).toBe(-32601);
  });

  it('says when the widget navigated away, pushes theme changes, and stops when disposed', () => {
    const h = harness();
    h.receive(notification('tnzi/widget-navigated'));
    expect(h.onNavigated).toHaveBeenCalledTimes(1);

    h.host.contextChanged();
    expect(h.sent().at(-1)).toEqual({ jsonrpc: '2.0', method: 'ui/notifications/host-context-changed', params: CONTEXT });

    h.host.dispose();
    h.receive(notification('ui/notifications/sandbox-proxy-ready'));
    h.host.contextChanged();
    expect(h.sent()).toHaveLength(1);
    expect(h.page.removeEventListener).toHaveBeenCalled();
  });
});

describe('widgetLinkUrl / widgetMessageText', () => {
  it('accepts absolute http(s) only', () => {
    expect(widgetLinkUrl('https://a.example/x')).toBe('https://a.example/x');
    expect(widgetLinkUrl('/relative')).toBeNull();
    expect(widgetLinkUrl('data:text/html,hi')).toBeNull();
    expect(widgetLinkUrl(42)).toBeNull();
  });

  it('joins text blocks', () => {
    expect(widgetMessageText({ content: [{ type: 'text', text: 'a' }, { type: 'image' }, { type: 'text', text: 'b' }] })).toBe('a\nb');
    expect(widgetMessageText({ content: { type: 'text', text: ' x ' } })).toBe('x');
    expect(widgetMessageText({})).toBeNull();
  });
});
