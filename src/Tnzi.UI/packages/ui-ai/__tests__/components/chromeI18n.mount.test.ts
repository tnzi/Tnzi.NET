/**
 * Chrome copy outside the chat shell follows the dictionary.
 *
 * The keys existed (`palette.*`, `embed.*`, `artifact.*`, `chat.copied`) but
 * the components rendered English literals, so a product that provided the
 * Chinese dictionary still showed an English command palette, embed header,
 * streaming indicator and copy/download labels.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, reactive, type App, type Component } from 'vue';

const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');
const { default: TCommandPalette } = await import('../../src/components/overlay/TCommandPalette.vue');
const { default: TFloatingChat } = await import('../../src/embed/TFloatingChat.vue');
const { default: TSidebarChat } = await import('../../src/embed/TSidebarChat.vue');
const { default: TMessageList } = await import('../../src/components/chat/TMessageList.vue');
const { default: TArtifactPanel } = await import('../../src/components/artifact/TArtifactPanel.vue');
const { default: TCodeBlock } = await import('../../src/components/streaming/TCodeBlock.vue');
const { default: TMessageActions } = await import('../../src/components/chat/TMessageActions.vue');
const { default: TSettingsDialog } = await import('../../src/components/overlay/TSettingsDialog.vue');
const { default: TStreamMarkdown } = await import('../../src/components/streaming/TStreamMarkdown.vue');
const { default: TStreamLoader } = await import('../../src/components/streaming/TStreamLoader.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mountZh(component: Component, props: Record<string, unknown>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(zhCn);
      return () => h(component, props);
    },
  });
  app = createApp(Root);
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('dictionary-backed chrome', () => {
  it('TCommandPalette', async () => {
    mountZh(TCommandPalette, { modelValue: true, actions: [] });
    await settle();

    expect(document.querySelector('.t-cmdk__input')?.getAttribute('placeholder')).toBe(zhCn.palette.placeholder);
    expect(document.querySelector('.t-cmdk__empty')?.textContent?.trim()).toBe(zhCn.palette.noResults);
    expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-label')).toBe(zhCn.sidebar.commandPalette);
    expect(document.querySelector('.t-cmdk__close')?.getAttribute('aria-label')).toBe(zhCn.common.close);
  });

  it('TFloatingChat', async () => {
    const el = mountZh(TFloatingChat, { messages: [] });
    await settle();
    const fab = el.querySelector('.t-floating-chat__fab') as HTMLButtonElement;
    expect(fab.getAttribute('aria-label')).toBe(zhCn.embed.open);

    fab.click();
    await settle();
    expect(el.querySelector('.t-floating-chat__header')?.textContent).toContain(zhCn.embed.title);
    expect(fab.getAttribute('aria-label')).toBe(zhCn.embed.close);
  });

  it('TSidebarChat', async () => {
    const props = reactive({ messages: [], open: false });
    const el = mountZh(TSidebarChat, props);
    await settle();
    props.open = true;
    await settle();
    expect(el.querySelector('.t-sidebar-chat__header')?.textContent).toContain(zhCn.embed.title);
    expect(el.querySelector('.t-sidebar-chat__header button')?.getAttribute('aria-label')).toBe(zhCn.embed.close);
  });

  it('TMessageList streaming indicator', async () => {
    const el = mountZh(TMessageList, {
      messages: [{ id: 'a', role: 'assistant', content: '', createdAt: '2026-01-01T00:00:00Z', isStreaming: true }],
    });
    await settle();
    expect(el.textContent).toContain(zhCn.chat.generating);
    expect(el.textContent).not.toContain('Generating');
  });

  it('TArtifactPanel tabs and toolbar', async () => {
    const el = mountZh(TArtifactPanel, { artifact: { title: 'index.html', content: '<p>hi</p>' } });
    await settle();
    const tabs = [...el.querySelectorAll('.t-artifact-panel__tab span')].map((s) => s.textContent?.trim());
    expect(tabs).toEqual([zhCn.artifact.preview, zhCn.artifact.code]);
    const tools = [...el.querySelectorAll('.t-artifact-panel__tool')].map((b) => b.getAttribute('aria-label'));
    expect(tools).toEqual([zhCn.chat.copyCode, zhCn.artifact.download, zhCn.artifact.openExternal]);
  });

  it('TSettingsDialog title', async () => {
    mountZh(TSettingsDialog, { modelValue: true, sections: [] });
    await settle();
    expect(document.querySelector('.t-settings-backdrop')?.getAttribute('aria-label')).toBe(zhCn.sidebar.settings);
  });

  it('TMessageActions copy button', async () => {
    const el = mountZh(TMessageActions, { content: 'answer' });
    await settle();
    expect(el.querySelector('.t-message-actions__btn--icon')?.getAttribute('aria-label')).toBe(zhCn.chat.copy);
  });

  it('TCodeBlock copy button', async () => {
    const el = mountZh(TCodeBlock, { code: 'const a = 1', language: 'ts' });
    await settle();
    expect(el.querySelector('.t-code-block__header button')?.getAttribute('aria-label')).toBe(zhCn.chat.copyCode);
  });

  // The copy button on a fenced code block in an answer is rendered into the
  // markdown HTML, which used to carry English literals.
  it('TStreamMarkdown code block copy button, and its "copied" confirmation', async () => {
    const el = mountZh(TStreamMarkdown, { content: ['```ts', 'const a = 1', '```'].join('\n') });
    await settle();
    // Icon-only: the name is the accessible label and the tooltip.
    const button = el.querySelector<HTMLButtonElement>('.t-md-code__copy')!;
    expect(button.textContent?.trim()).toBe('');
    expect(button.getAttribute('aria-label')).toBe(zhCn.chat.copyCode);
    expect(button.getAttribute('title')).toBe(zhCn.chat.copyCode);

    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async () => {} }, configurable: true });
    button.click();
    expect(button.getAttribute('aria-label')).toBe(zhCn.chat.copied);
    expect(button.classList.contains('is-copied')).toBe(true);
  });

  it('TStreamMarkdown code block follows a language switch', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    let messages: ReturnType<typeof createAiI18n> | null = null;
    const Root = defineComponent({
      setup() {
        messages = createAiI18n(zhCn);
        return () => h(TStreamMarkdown, { content: ['```ts', 'const a = 1', '```'].join('\n') });
      },
    });
    app = createApp(Root);
    app.mount(host);
    await settle();
    expect(host.querySelector('.t-md-code__copy')?.getAttribute('aria-label')).toBe(zhCn.chat.copyCode);

    messages!.value = en;
    await settle();
    expect(host.querySelector('.t-md-code__copy')?.getAttribute('aria-label')).toBe(en.chat.copyCode);
    expect(host.querySelector('.t-md-code__copy')?.getAttribute('title')).toBe(en.chat.copyCode);
  });

  it('TStreamLoader accessible name, and no English drawing title', async () => {
    const el = mountZh(TStreamLoader, {});
    await settle();
    expect(el.querySelector('[role="status"]')?.getAttribute('aria-label')).toBe(zhCn.common.loading);
    expect(el.querySelector('svg title')).toBeNull();
    expect(el.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('TStreamLoader takes a label of its own', async () => {
    const el = mountZh(TStreamLoader, { label: 'Running read_file' });
    await settle();
    expect(el.querySelector('[role="status"]')?.getAttribute('aria-label')).toBe('Running read_file');
  });
});

describe('catalogue', () => {
  const values = (messages: typeof en) =>
    Object.entries(messages).flatMap(([section, entries]) =>
      Object.entries(entries as Record<string, string>).map(([key, value]) => [`${section}.${key}`, value] as const),
    );

  it('stores no replacement characters (a lossy re-encode once garbled three zh-cn strings)', () => {
    expect(values(zhCn).filter(([, value]) => value.includes('�'))).toEqual([]);
    expect(values(en).filter(([, value]) => value.includes('�'))).toEqual([]);
  });

  it('translates the keys the shell and chrome defaults read', () => {
    const keys = [
      'chat.generating',
      'chat.copyCode',
      'chat.agentName',
      'chat.threadsLabel',
      'chat.deleteConfirm',
      'landing.greeting',
      'landing.placeholder',
      'chat.openingThread',
      'composer.voiceMicBlocked',
      'composer.voiceMicNotGranted',
      'composer.voiceServiceNotAllowed',
      'composer.voiceNoMicrophone',
      'composer.voiceNetwork',
      'composer.voiceNoSpeech',
      'composer.voiceLanguageNotSupported',
      'composer.voiceAborted',
      'composer.voiceUnavailable',
      'settings.security',
      'settings.personalization',
      'settings.about',
    ];
    const zh = new Map(values(zhCn));
    const english = new Map(values(en));
    expect(keys.filter((key) => !zh.get(key) || zh.get(key) === english.get(key))).toEqual([]);
  });
});
