/**
 * The host side of a widget frame: JSON-RPC 2.0 over `postMessage`, with the message names of the
 * MCP Apps extension (io.modelcontextprotocol/ui) where they overlap.
 *
 * The frame holds the widget runner (a sandbox proxy); the widget's document runs in the runner's
 * inner frame, and the runner relays its messages unchanged. Only messages whose source is this
 * frame's window are read; anything else on the page (another widget, another frame, the page
 * itself) is ignored.
 *
 * What a widget may ask for:
 *   - `ui/initialize` → the host context (theme, variables, display mode, locale) and capabilities;
 *   - `ui/notifications/size-changed` → `onSize` (the frame's height follows the document);
 *   - `ui/open-link` → `http(s)` only, in a new tab (`handlers.openLink` to route it elsewhere);
 *   - `ui/message` → off unless the product gives `handlers.sendMessage`, and then only once the
 *     user confirms the text (`confirmMessage`);
 *   - `tools/call` → off unless the product gives `handlers.callTool` and lists the tool in
 *     `handlers.allowedTools`.
 * Everything else is answered "method not found".
 */
import { MCP_APPS_PROTOCOL_VERSION, type WidgetHostContext } from './widget-document';

/** What a product lets widgets do beyond showing themselves. Every member is optional. */
export interface WidgetHostHandlers {
  /** Opens a link the widget asked for (an `http(s)` URL). Default: a new tab, without opener. */
  readonly openLink?: (url: string) => void | Promise<void>;
  /**
   * Posts a message into the conversation as the user (`ui/message`). Without it a widget cannot.
   * The user confirms every message before this is called.
   */
  readonly sendMessage?: (text: string) => void | Promise<void>;
  /** Calls a tool for the widget (`tools/call`). Without it, or for a tool not in `allowedTools`, a widget cannot. */
  readonly callTool?: (name: string, args: Record<string, unknown>) => Promise<unknown>;
  /** The tools a widget may call through `callTool`, by exact name. */
  readonly allowedTools?: readonly string[];
}

export interface WidgetHostOptions {
  /** The window the frame posts to (the page's). */
  readonly window: Window;
  /** The frame's window, once it has one. */
  readonly frame: () => Window | null | undefined;
  /** The widget's document (see `composeWidgetDocument`). */
  readonly document: string;
  /** The widget's title. */
  readonly title: string;
  /** The current host context; read whenever it is sent. */
  readonly context: () => WidgetHostContext;
  /** The document's size changed. */
  readonly onSize?: (size: { readonly width: number | null; readonly height: number }) => void;
  /** The widget navigated its frame away and was removed by the runner. */
  readonly onNavigated?: () => void;
  /** Asks the user whether the widget may send `text` as their message. */
  readonly confirmMessage?: (text: string) => Promise<boolean>;
  readonly handlers?: WidgetHostHandlers;
}

export interface WidgetHost {
  /** Tell the widget the host context changed (the theme). */
  contextChanged(): void;
  /** Stop listening. */
  dispose(): void;
}

/** The longest message a widget may send into the conversation. */
export const WIDGET_MAX_MESSAGE_LENGTH = 4000;

/** A height beyond any page: a widget reporting more is clamped by the host anyway. */
const MAX_REPORTED_HEIGHT = 100_000;

const METHOD_NOT_FOUND = -32601;
const INVALID_PARAMS = -32602;
const REFUSED = -32000;

interface RpcMessage {
  readonly jsonrpc: '2.0';
  readonly id?: string | number;
  readonly method?: string;
  readonly params?: unknown;
}

class RpcError extends Error {
  readonly code: number;

  constructor(code: number, message: string) {
    super(message);
    this.code = code;
  }
}

function isRpcMessage(value: unknown): value is RpcMessage {
  return Boolean(value) && typeof value === 'object' && (value as { jsonrpc?: unknown }).jsonrpc === '2.0';
}

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

/** An absolute `http(s)` URL, or null. */
export function widgetLinkUrl(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

/** The text of a `ui/message` request: its text blocks joined, or null when there is none. */
export function widgetMessageText(params: unknown): string | null {
  const content = record(params).content;
  const blocks = Array.isArray(content) ? content : [content];
  const text = blocks
    .map(record)
    .filter((block) => block.type === 'text' && typeof block.text === 'string')
    .map((block) => block.text as string)
    .join('\n')
    .trim();
  return text || null;
}

export function createWidgetHost(options: WidgetHostOptions): WidgetHost {
  const { handlers = {} } = options;
  let disposed = false;
  let initialized = false;

  function post(message: object): void {
    if (disposed) return;
    options.frame()?.postMessage(message, '*');
  }

  function notify(method: string, params: object): void {
    post({ jsonrpc: '2.0', method, params });
  }

  async function answer(message: RpcMessage): Promise<unknown> {
    const params = record(message.params);
    switch (message.method) {
      case 'ui/initialize':
        return {
          protocolVersion: MCP_APPS_PROTOCOL_VERSION,
          hostInfo: { name: '@tnzi/ui-ai', version: '1' },
          hostCapabilities: {
            openLinks: {},
            ...(handlers.sendMessage ? { message: {} } : {}),
            ...(handlers.callTool && handlers.allowedTools?.length ? { serverTools: {} } : {}),
          },
          hostContext: options.context(),
        };
      case 'ui/open-link': {
        const url = widgetLinkUrl(params.url);
        if (!url) throw new RpcError(INVALID_PARAMS, 'Only absolute http and https links can be opened');
        // Only right after the user clicked: a click inside the frame activates this page too.
        // Without it a widget could open pages on its own through a product's handler, which no
        // popup blocker stands in front of. Engines without `userActivation` are let through.
        if (options.window.navigator?.userActivation?.isActive === false) {
          throw new RpcError(REFUSED, 'Links open only when the user clicks one');
        }
        if (handlers.openLink) await handlers.openLink(url);
        else options.window.open(url, '_blank', 'noopener,noreferrer');
        return {};
      }
      case 'ui/message': {
        if (!handlers.sendMessage) throw new RpcError(METHOD_NOT_FOUND, 'This host does not let widgets send messages');
        const text = widgetMessageText(params);
        if (!text || text.length > WIDGET_MAX_MESSAGE_LENGTH) throw new RpcError(INVALID_PARAMS, 'The message is empty or too long');
        const confirmed = options.confirmMessage ? await options.confirmMessage(text) : false;
        if (!confirmed) throw new RpcError(REFUSED, 'The user did not send the message');
        await handlers.sendMessage(text);
        return {};
      }
      case 'tools/call': {
        const name = typeof params.name === 'string' ? params.name : '';
        if (!handlers.callTool || !handlers.allowedTools?.includes(name)) {
          throw new RpcError(METHOD_NOT_FOUND, `This host does not let widgets call '${name}'`);
        }
        return await handlers.callTool(name, record(params.arguments));
      }
      default:
        throw new RpcError(METHOD_NOT_FOUND, 'Method not found');
    }
  }

  async function respond(message: RpcMessage): Promise<void> {
    const id = message.id as string | number;
    try {
      const result = await answer(message);
      post({ jsonrpc: '2.0', id, result });
    } catch (error) {
      const code = error instanceof RpcError ? error.code : REFUSED;
      const text = error instanceof Error ? error.message : 'The host could not do that';
      post({ jsonrpc: '2.0', id, error: { code, message: text } });
    }
  }

  function onNotification(message: RpcMessage): void {
    const params = record(message.params);
    switch (message.method) {
      case 'ui/notifications/sandbox-proxy-ready':
        notify('ui/notifications/sandbox-resource-ready', {
          html: options.document,
          title: options.title,
          colorScheme: options.context().theme,
        });
        return;
      case 'ui/notifications/initialized':
        if (initialized) return;
        initialized = true;
        // The document is the widget; its title is the only other argument worth handing it.
        notify('ui/notifications/tool-input', { arguments: { title: options.title } });
        return;
      case 'ui/notifications/size-changed': {
        const height = Number(params.height);
        if (!Number.isFinite(height) || height < 0) return;
        const width = Number(params.width);
        options.onSize?.({
          width: Number.isFinite(width) && width >= 0 ? width : null,
          height: Math.min(Math.ceil(height), MAX_REPORTED_HEIGHT),
        });
        return;
      }
      case 'tnzi/widget-navigated':
        options.onNavigated?.();
        return;
      default:
        return;
    }
  }

  function onMessage(event: MessageEvent): void {
    if (disposed) return;
    const frame = options.frame();
    if (!frame || event.source !== frame) return;
    const message = event.data;
    if (!isRpcMessage(message) || typeof message.method !== 'string') return;
    if (message.id !== undefined && message.id !== null) void respond(message);
    else onNotification(message);
  }

  options.window.addEventListener('message', onMessage);

  return {
    contextChanged() {
      notify('ui/notifications/host-context-changed', options.context());
    },
    dispose() {
      disposed = true;
      options.window.removeEventListener('message', onMessage);
    },
  };
}
