import { describe, it, expect, vi, beforeEach } from 'vitest';

// The widget SFCs play no part in what is tested and this package's vitest
// config has no Vue SFC plugin (unit tests target the headless layer), so stub
// them with a renderless component. The inline stub records the props it was
// last rendered with so a test can read the message list the widget would show.
const rendered = vi.hoisted(() => ({
  messages: [] as unknown[],
  enableAttachments: undefined as boolean | undefined,
  send: undefined as ((content: string, files: File[]) => void) | undefined,
  regenerate: undefined as ((messageId: string) => void) | undefined,
}));
vi.mock('../../src/embed/TFloatingChat.vue', () => ({ default: { render: () => null } }));
vi.mock('../../src/embed/TSidebarChat.vue', () => ({ default: { render: () => null } }));
vi.mock('../../src/embed/TInlineChat.vue', () => ({
  default: {
    props: ['messages', 'enableAttachments', 'onSend', 'onRegenerate'],
    render(this: {
      messages: unknown[];
      enableAttachments?: boolean;
      onSend?: (c: string, f: File[]) => void;
      onRegenerate?: (id: string) => void;
    }) {
      rendered.messages = this.messages;
      rendered.enableAttachments = this.enableAttachments;
      rendered.send = this.onSend;
      rendered.regenerate = this.onRegenerate;
      return null;
    },
  },
}));

const streamChat = vi.fn(async () => ({ text: 'ok', reasoning: '', completed: true, error: null }));
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: unknown) => streamChat(opts as never),
}));

const { createTnziChat } = await import('../../src/embed/createTnziChat');

type Call = { headers?: Record<string, string> };
const headersOfCall = (index: number) => (streamChat.mock.calls[index] as unknown as [Call])[0].headers;

/**
 * Server-backed mode used to take `headers` as a plain object and reuse it for
 * every turn, so an `Authorization` header captured at creation time went
 * stale the moment the access token rotated. Accepting a function lets the
 * host resolve the headers per turn.
 */
describe('createTnziChat server-backed headers', () => {
  beforeEach(() => {
    streamChat.mockClear();
    document.body.innerHTML = '<div id="host"></div>';
  });

  it('resolves a headers() function on every turn', async () => {
    let token = 'first';
    const chat = createTnziChat({
      mode: 'inline',
      el: '#host',
      apiBaseUrl: '/api',
      headers: () => ({ Authorization: `Bearer ${token}` }),
    });

    chat.sendMessage('one');
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(1));
    token = 'second';
    chat.sendMessage('two');
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(2));

    expect(headersOfCall(0)).toEqual({ Authorization: 'Bearer first' });
    expect(headersOfCall(1)).toEqual({ Authorization: 'Bearer second' });
    chat.destroy();
  });

  it('still accepts a static headers object', async () => {
    const chat = createTnziChat({
      mode: 'inline',
      el: '#host',
      apiBaseUrl: '/api',
      headers: { 'X-Static': 'yes' },
    });

    chat.sendMessage('one');
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(1));

    expect(headersOfCall(0)).toEqual({ 'X-Static': 'yes' });
    chat.destroy();
  });
});

type Row = { role: string; content: string; status?: string; error?: string | null; isStreaming?: boolean };
const rows = () => rendered.messages as Row[];

/**
 * A failed server turn must leave a row the renderer can show as an error.
 * `TChatMessage` reads `status === 'error'` / `error`; without them a stream
 * that dies renders as an assistant that said nothing.
 */
describe('createTnziChat server-backed failure', () => {
  beforeEach(() => {
    streamChat.mockClear();
    document.body.innerHTML = '<div id="host"></div>';
  });

  it('sets status error and the error text on the assistant row', async () => {
    streamChat.mockResolvedValueOnce({ text: '', reasoning: '', completed: false, error: new Error('upstream 502') });
    const onError = vi.fn();
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api', onError });

    chat.sendMessage('hello');
    await vi.waitFor(() => expect(rows().at(-1)?.status).toBe('error'));

    const assistant = rows().at(-1)!;
    expect(assistant.role).toBe('assistant');
    expect(assistant.error).toBe('upstream 502');
    expect(assistant.isStreaming).toBe(false);
    expect(onError).toHaveBeenCalledTimes(1);
    chat.destroy();
  });

  it('marks a completed turn done', async () => {
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    chat.sendMessage('hello');
    await vi.waitFor(() => expect(rows().at(-1)?.status).toBe('done'));
    expect(rows().at(-1)?.error).toBeUndefined();
    chat.destroy();
  });
});

/**
 * The error block `TChatMessage` renders carries a Retry button that emits
 * `regenerate`. The embed shells relayed only send / stop, so on the
 * server-backed path the button was a dead control: every failed turn showed
 * a Retry that did nothing when clicked.
 */
describe('createTnziChat regenerate', () => {
  type RowWithId = Row & { id: string; attachments?: unknown[] };
  const rowsWithId = () => rendered.messages as RowWithId[];
  type Body = { message: string; content?: unknown[] };
  const bodyOfCall = (index: number) => (streamChat.mock.calls[index] as unknown as [{ body: Body }])[0].body;
  const png = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'shot.png', { type: 'image/png' });

  beforeEach(() => {
    streamChat.mockClear();
    document.body.innerHTML = '<div id="host"></div>';
  });

  it('the shell receives a regenerate handler', async () => {
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    await vi.waitFor(() => expect(rendered.regenerate).toBeDefined());
    chat.destroy();
  });

  it('server mode re-asks the preceding user turn once, with its attachments, and drops the failed rows', async () => {
    streamChat.mockResolvedValueOnce({ text: '', reasoning: '', completed: false, error: new Error('upstream 502') });
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    await vi.waitFor(() => expect(rendered.send).toBeDefined());
    rendered.send!('what is this?', [png()]);
    await vi.waitFor(() => expect(rowsWithId().at(-1)?.status).toBe('error'));
    const failed = rowsWithId().at(-1)!;

    rendered.regenerate!(failed.id);
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(rowsWithId().at(-1)?.status).toBe('done'));

    // Exactly one copy of the question, then the new answer.
    expect(rowsWithId().map((r) => r.role)).toEqual(['user', 'assistant']);
    expect(rowsWithId()[0].content).toBe('what is this?');
    expect(rowsWithId()[0].attachments).toHaveLength(1);
    expect(rowsWithId().at(-1)!.id).not.toBe(failed.id);
    // The retry carries the same text and image parts as the first attempt.
    expect(bodyOfCall(1)).toEqual(bodyOfCall(0));
    chat.destroy();
  });

  it('BYO mode hands the preceding user text back to onSend', async () => {
    const onSend = vi.fn(async (_text: string, api: { pushMessage: (m: { role: 'assistant'; content: string }) => string }) => {
      api.pushMessage({ role: 'assistant', content: 'reply' });
    });
    const chat = createTnziChat({ mode: 'inline', el: '#host', onSend });
    chat.sendMessage('hello');
    await vi.waitFor(() => expect(rowsWithId()).toHaveLength(2));

    rendered.regenerate!(rowsWithId()[1].id);
    await vi.waitFor(() => expect(onSend).toHaveBeenCalledTimes(2));
    expect(onSend.mock.calls[1][0]).toBe('hello');
    await vi.waitFor(() => expect(rowsWithId()).toHaveLength(2));
    expect(rowsWithId().map((r) => r.role)).toEqual(['user', 'assistant']);
    chat.destroy();
  });

  it('ignores a regenerate while a turn is streaming, and for a user row', async () => {
    let release: (() => void) | undefined;
    streamChat.mockImplementationOnce(() => new Promise((r) => {
      release = () => r({ text: 'late', reasoning: '', completed: true, error: null });
    }));
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    chat.sendMessage('hello');
    await vi.waitFor(() => expect(rowsWithId()).toHaveLength(2));

    rendered.regenerate!(rowsWithId()[1].id);
    rendered.regenerate!(rowsWithId()[0].id);
    expect(streamChat).toHaveBeenCalledTimes(1);
    expect(rowsWithId()).toHaveLength(2);
    release!();
    await vi.waitFor(() => expect(rowsWithId().at(-1)?.status).toBe('done'));
    chat.destroy();
  });
});

/**
 * The widgets emit `send(content, files)`; `createTnziChat` took the first
 * argument only, so the paperclip every embed showed collected files that
 * never left the browser.
 */
describe('createTnziChat attachments', () => {
  const png = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'shot.png', { type: 'image/png' });
  const pdf = () => new File([new Uint8Array([1, 2, 3])], 'doc.pdf', { type: 'application/pdf' });
  type Body = { message: string; content?: unknown[] };
  const bodyOfCall = (index: number) => (streamChat.mock.calls[index] as unknown as [{ body: Body }])[0].body;

  beforeEach(() => {
    streamChat.mockClear();
    document.body.innerHTML = '<div id="host"></div>';
  });

  it('server mode forwards a widget send with files as content parts', async () => {
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    await vi.waitFor(() => expect(rendered.send).toBeDefined());
    rendered.send!('what is this?', [png()]);
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(1));

    expect(bodyOfCall(0).message).toBe('what is this?');
    expect(bodyOfCall(0).content).toEqual([
      { type: 'text', text: 'what is this?' },
      { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png' },
    ]);
    expect(rows()[0]).toMatchObject({ role: 'user', content: 'what is this?' });
    expect((rows()[0] as { attachments?: unknown[] }).attachments).toHaveLength(1);
    chat.destroy();
  });

  it('server mode sends a files-only turn with the attachment names as its message', async () => {
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    await vi.waitFor(() => expect(rendered.send).toBeDefined());
    rendered.send!('', [png()]);
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(1));

    // The backend adds and persists the user turn only when `message` is
    // non-blank; a blank one here is an image the model never sees.
    expect(bodyOfCall(0).message).toBe('[Attached: shot.png]');
    expect(bodyOfCall(0).content?.[0]).toEqual({ type: 'text', text: '[Attached: shot.png]' });
    expect(rows()[0]).toMatchObject({ role: 'user', content: '' });
    chat.destroy();
  });

  it('server mode refuses a non-image file through onError and sends nothing', async () => {
    const onError = vi.fn();
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api', onError });
    await vi.waitFor(() => expect(rendered.send).toBeDefined());
    rendered.send!('summarise', [pdf()]);
    await vi.waitFor(() => expect(onError).toHaveBeenCalledTimes(1));

    expect(onError.mock.calls[0][0].message).toContain('doc.pdf');
    expect(streamChat).not.toHaveBeenCalled();
    expect(rows()).toEqual([]);
    chat.destroy();
  });

  it('BYO mode hands the files to onSend', async () => {
    const onSend = vi.fn();
    const chat = createTnziChat({ mode: 'inline', el: '#host', onSend });
    await vi.waitFor(() => expect(rendered.send).toBeDefined());
    const file = png();
    rendered.send!('look', [file]);
    await vi.waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    expect(onSend.mock.calls[0][0]).toBe('look');
    expect(onSend.mock.calls[0][2]).toEqual([file]);
    chat.destroy();
  });

  it('enableAttachments reaches the widget and defaults to on', async () => {
    const a = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    await vi.waitFor(() => expect(rendered.enableAttachments).toBe(true));
    a.destroy();
    document.body.innerHTML = '<div id="host"></div>';
    const b = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api', enableAttachments: false });
    await vi.waitFor(() => expect(rendered.enableAttachments).toBe(false));
    b.destroy();
  });

  it('sendMessage accepts files too', async () => {
    const chat = createTnziChat({ mode: 'inline', el: '#host', apiBaseUrl: '/api' });
    chat.sendMessage('', [png()]);
    await vi.waitFor(() => expect(streamChat).toHaveBeenCalledTimes(1));
    expect(bodyOfCall(0).content?.map((p) => (p as { type: string }).type)).toEqual(['text', 'image']);
    expect(bodyOfCall(0).message).toBe('[Attached: shot.png]');
    chat.destroy();
  });
});
