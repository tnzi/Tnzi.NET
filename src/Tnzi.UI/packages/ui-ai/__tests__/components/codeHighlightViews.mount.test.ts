/**
 * The two code views that render streamed / switched code through Shiki.
 *
 * Both have to follow the language as well as the code (an artifact switched
 * from `.ts` to `.py` with identical text was left coloured as TypeScript), and
 * neither may let a late Shiki pass overwrite a newer one: highlighting is two
 * awaits deep, so switching input mid-pass used to paint the previous input.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, reactive, type App } from 'vue';

interface ShikiCall {
  code: string;
  lang: string;
  resolve: (html: string) => void;
}
const pending: ShikiCall[] = [];

vi.mock('shiki', () => ({
  codeToHtml: (code: string, options: { lang: string }) =>
    new Promise<string>((resolve) => {
      pending.push({ code, lang: options.lang, resolve });
    }),
}));

const { default: TArtifactCodeView } = await import('../../src/components/artifact/TArtifactCodeView.vue');
const { default: TCodeBlock } = await import('../../src/components/streaming/TCodeBlock.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mountView(component: unknown, props: Record<string, unknown>) {
  const state = reactive({ ...props });
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(component as never, state) });
  app.mount(host);
  return { host, state };
}

/** Let the dynamic `import('shiki')` and the awaits after it run. */
async function settle(): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await vi.advanceTimersByTimeAsync(0);
    await nextTick();
  }
}

beforeEach(() => {
  vi.useFakeTimers();
  pending.length = 0;
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  vi.useRealTimers();
});

describe.each([
  ['TArtifactCodeView', () => TArtifactCodeView],
  ['TCodeBlock', () => TCodeBlock],
])('%s', (_name, component) => {
  it('highlights again when only the language changes', async () => {
    const { state } = mountView(component(), { code: 'print(1)', language: 'typescript' });
    await settle();
    expect(pending.map((c) => c.lang)).toEqual(['typescript']);
    pending[0]!.resolve('<pre>ts</pre>');
    await settle();

    state.language = 'python';
    await vi.advanceTimersByTimeAsync(200);
    await settle();

    expect(pending.map((c) => c.lang)).toEqual(['typescript', 'python']);
  });

  it('keeps the newer result when an older pass resolves last', async () => {
    const { host, state } = mountView(component(), { code: 'first', language: 'text' });
    await settle();

    state.code = 'second';
    await vi.advanceTimersByTimeAsync(200);
    await settle();
    expect(pending.map((c) => c.code)).toEqual(['first', 'second']);

    pending[1]!.resolve('<pre class="shiki">SECOND</pre>');
    await settle();
    pending[0]!.resolve('<pre class="shiki">FIRST</pre>');
    await settle();

    expect(host.innerHTML).toContain('SECOND');
    expect(host.innerHTML).not.toContain('FIRST');
  });
});
