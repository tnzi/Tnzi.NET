/**
 * Files placed in the composer from outside: a desktop host's capture button,
 * files the operating system hands over ("send to", a drop on a tray icon).
 *
 * The contract is that they behave exactly like a drop (same size and type
 * checks, same `attachments-rejected` event) and wait in the composer for the
 * user's text, whichever composer is on screen. With a `#view` page in the main
 * area there is no composer: the files are held, the shell asks to show the
 * conversation, and they land when its composer appears.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App, type Ref } from 'vue';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');

type ChatAppInstance = InstanceType<typeof TChatApp>;
type ComposerInstance = InstanceType<typeof TThreadComposer>;

let app: App | null = null;
let host: HTMLElement | null = null;

function file(name: string, size: number, type: string): File {
  const f = new File(['x'], name, { type });
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

beforeEach(() => {
  globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock');
  globalThis.URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

function mountApp(render: () => ReturnType<typeof h>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
  return host;
}

function chipNames(el: HTMLElement): string[] {
  return Array.from(el.querySelectorAll('.t-thread-composer__chip-name')).map((n) => n.textContent?.trim() ?? '');
}

describe('TThreadComposer.addAttachments', () => {
  it('lands accepted files as chips and refuses the rest through the same event as a drop', async () => {
    const composer = ref<ComposerInstance | null>(null);
    const rejected = vi.fn();
    const el = mountApp(() =>
      h(TThreadComposer, {
        ref: composer,
        modelValue: '',
        enableAttachments: true,
        accept: '.pdf',
        onAttachmentsRejected: rejected,
      }),
    );
    await settle();

    const result = composer.value!.addAttachments([file('brief.pdf', 10, 'application/pdf'), file('tool.exe', 10, 'application/x-msdownload')]);
    await settle();

    expect(chipNames(el)).toEqual(['brief.pdf']);
    expect(result?.added.map((f) => f.name)).toEqual(['brief.pdf']);
    expect(rejected).toHaveBeenCalledTimes(1);
    expect(rejected.mock.calls[0]?.[0].map((r: { reason: string }) => r.reason)).toEqual(['unsupported-type']);
    expect(el.querySelector('.t-thread-composer__reject-text')?.textContent).toContain('tool.exe');
  });

  it('is null when attachments are off, and adds nothing', async () => {
    const composer = ref<ComposerInstance | null>(null);
    const el = mountApp(() => h(TThreadComposer, { ref: composer, modelValue: '', enableAttachments: false }));
    await settle();

    expect(composer.value!.addAttachments([file('a.pdf', 10, 'application/pdf')])).toBeNull();
    await settle();
    expect(chipNames(el)).toEqual([]);
  });
});

describe('TChatApp.addAttachments', () => {
  function mountShell(props: Record<string, unknown>, view?: Ref<string>) {
    const shell = ref<ChatAppInstance | null>(null);
    const events = { rejected: vi.fn(), viewChanges: [] as string[] };
    const el = mountApp(() =>
      h(TChatApp, {
        ref: shell,
        sidebarStorageKey: null,
        autoApplyTheme: false,
        enableAttachments: true,
        ...props,
        ...(view
          ? {
              view: view.value,
              'onUpdate:view': (v: string) => {
                events.viewChanges.push(v);
                view.value = v;
              },
            }
          : {}),
        onAttachmentsRejected: events.rejected,
      }),
    );
    return { el, shell, events };
  }

  it('lands in the landing composer when there is no conversation yet', async () => {
    const { el, shell } = mountShell({});
    await settle();

    const result = shell.value!.addAttachments([file('shot.png', 10, 'image/png')]);
    await settle();

    expect(el.querySelector('.t-landing .t-thread-composer__chip')).not.toBeNull();
    expect(result).toEqual({ added: [expect.any(File)], rejected: [], pending: [] });
  });

  it('lands in the thread composer once a conversation is open', async () => {
    const messages = [{ id: 'm1', role: 'user', content: 'hi', createdAt: new Date().toISOString() }];
    const { el, shell } = mountShell({ messages });
    await settle();

    shell.value!.addAttachments([file('notes.pdf', 10, 'application/pdf')]);
    await settle();

    expect(el.querySelector('.t-landing')).toBeNull();
    expect(chipNames(el)).toEqual(['notes.pdf']);
  });

  it('applies the shell size limit and forwards refusals', async () => {
    const { el, shell, events } = mountShell({ composerMaxFileSize: 100 });
    await settle();

    const result = shell.value!.addAttachments([file('big.pdf', 500, 'application/pdf')]);
    await settle();

    expect(result?.rejected.map((r) => r.reason)).toEqual(['too-large']);
    expect(events.rejected).toHaveBeenCalledTimes(1);
    expect(chipNames(el)).toEqual([]);
  });

  it('holds the files while a #view page fills the main area, asks for the conversation, and lands them when it shows', async () => {
    const view = ref('agents');
    const { el, shell, events } = mountShell({}, view);
    await settle();
    expect(el.querySelector('.t-thread-composer')).toBeNull();

    const result = shell.value!.addAttachments([file('scan.pdf', 10, 'application/pdf')]);
    expect(result?.added).toEqual([]);
    expect(result?.pending.map((f) => f.name)).toEqual(['scan.pdf']);
    expect(events.viewChanges).toEqual(['chat']);

    await settle();
    expect(chipNames(el)).toEqual(['scan.pdf']);
  });

  it('is null when attachments are off', async () => {
    const { shell } = mountShell({ enableAttachments: false });
    await settle();
    expect(shell.value!.addAttachments([file('a.pdf', 10, 'application/pdf')])).toBeNull();
  });
});
