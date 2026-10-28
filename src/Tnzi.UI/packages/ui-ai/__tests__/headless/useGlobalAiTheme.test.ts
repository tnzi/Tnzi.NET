import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useGlobalAiTheme, AI_THEME_SCOPE } from '../../src/headless/useGlobalAiTheme';
import { buildAiThemeSnapshot } from '../../src/theme/snapshot';
import { createApp, defineComponent, h } from 'vue';
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui';

function client(overrides: Record<string, unknown> = {}) {
  return {
    get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: null } })),
    put: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ succeeded: true, code: 200, data: undefined })),
    post: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    ...overrides,
  } as never;
}

beforeEach(() => {
  document.documentElement.style.cssText = '';
});

describe('useGlobalAiTheme', () => {
  it('does nothing without a client', async () => {
    const t = useGlobalAiTheme();
    await t.load();
    expect(t.remote.value).toBeNull();
    expect(await t.save()).toBe(false);
    expect(t.canManage.value).toBe(false);
  });

  it('reads the chat scope by default', async () => {
    const c = client();
    await useGlobalAiTheme({ client: c }).load();
    expect((c as unknown as { get: ReturnType<typeof vi.fn> }).get).toHaveBeenCalledWith(
      `/appearance/theme/${AI_THEME_SCOPE}`,
    );
  });

  it('applies what it loaded', async () => {
    const snapshot = buildAiThemeSnapshot({ primary: '#7c3aed', ai: { bg: '#101014' } });
    const c = client({
      get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: snapshot } })),
    });

    const t = useGlobalAiTheme({ client: c });
    await t.load();

    expect(t.remote.value?.ai.bg).toBe('#101014');
    expect(document.documentElement.style.getPropertyValue('--tnzi-ai-bg')).toBe('#101014');
  });

  /**
   * A document this client cannot read is treated as unset. Applying the
   * fragments it happens to recognise would render a half-broken theme that
   * looks like a product bug rather than a version mismatch.
   */
  it('treats an unrecognised document as unset', async () => {
    const c = client({
      get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: { version: 99 } } })),
    });

    const t = useGlobalAiTheme({ client: c });
    await t.load();

    expect(t.remote.value).toBeNull();
  });

  it('survives a failing endpoint', async () => {
    const c = client({
      get: vi.fn(async () => {
        throw new Error('offline');
      }),
    });

    const t = useGlobalAiTheme({ client: c });
    await expect(t.load()).resolves.toBeUndefined();
    expect(t.remote.value).toBeNull();
    expect(t.loading.value).toBe(false);
  });

  /**
   * The one place failures must NOT be swallowed: reporting success on a 403
   * would tell the operator their change reached every user when it reached
   * nobody.
   */
  it('reports a rejected publish instead of pretending it worked', async () => {
    const c = client({
      put: vi.fn(async () => ({ succeeded: false, code: 403, message: 'Forbidden' })),
    });

    const t = useGlobalAiTheme({ client: c });
    t.setDraft(buildAiThemeSnapshot({ ai: { bg: '#101014' } }));

    expect(await t.save()).toBe(false);
    expect(t.error.value).toBe('Forbidden');
    expect(t.remote.value).toBeNull();
  });

  it('publishes to the scoped endpoint and adopts the draft', async () => {
    const c = client();
    const t = useGlobalAiTheme({ client: c });
    const draft = buildAiThemeSnapshot({ ai: { bg: '#101014' } });
    t.setDraft(draft);

    expect(await t.save()).toBe(true);
    expect((c as unknown as { put: ReturnType<typeof vi.fn> }).put).toHaveBeenCalledWith(
      `/admin/appearance/theme/${AI_THEME_SCOPE}`,
      { theme: draft },
    );
    expect(t.remote.value).toBe(draft);
    expect(t.isDirty.value).toBe(false);
  });

  it('reset clears the server snapshot and the applied overrides', async () => {
    const c = client();
    const t = useGlobalAiTheme({ client: c });
    t.setDraft(buildAiThemeSnapshot({ ai: { bg: '#101014' } }));
    await t.save();

    expect(await t.reset()).toBe(true);
    expect(t.remote.value).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--tnzi-ai-bg')).toBe('');
  });

  /**
   * `exportedAt` is stamped per build, so comparing whole documents would call
   * two identical themes built a second apart "unsaved".
   */
  it('ignores the timestamp when deciding whether anything changed', async () => {
    const snapshot = buildAiThemeSnapshot({
      ai: { bg: '#101014' },
      now: () => new Date('2026-08-02T10:00:00Z'),
    });
    const c = client({
      get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: snapshot } })),
    });

    const t = useGlobalAiTheme({ client: c });
    await t.load();
    t.setDraft(
      buildAiThemeSnapshot({ ai: { bg: '#101014' }, now: () => new Date('2026-08-02T11:00:00Z') }),
    );

    expect(t.isDirty.value).toBe(false);
  });

  it('canManage needs both a client and the caller saying so', () => {
    expect(useGlobalAiTheme({ client: client() }).canManage.value).toBe(false);
    expect(useGlobalAiTheme({ canManage: () => true }).canManage.value).toBe(false);
    expect(useGlobalAiTheme({ client: client(), canManage: () => true }).canManage.value).toBe(true);
  });

  /**
   * The published brand colour goes through the host theme context: the context
   * re-writes `--tnzi-primary` on every change to its settings (a light/dark
   * switch included), so an inline value beside it did not survive one.
   */
  it('applies the brand colour through the theme context provided to the caller', async () => {
    const snapshot = buildAiThemeSnapshot({ primary: '#7c3aed' });
    const c = client({
      get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: snapshot } })),
    });
    const context = createThemeContext(mergeThemeSettings({ colors: { primary: '#0d9488' } }));
    let controller: ReturnType<typeof useGlobalAiTheme> | null = null;
    const app = createApp(
      defineComponent({
        setup() {
          controller = useGlobalAiTheme({ client: c });
          return () => h('div');
        },
      }),
    );
    app.provide(THEME_CONTEXT_KEY, context);
    app.mount(document.createElement('div'));

    await controller!.load();
    expect(context.settings.value.colors.primary).toBe('#7c3aed');

    await controller!.reset();
    expect(context.settings.value.colors.primary).toBe('#0d9488');
    app.unmount();
  });

  it('autoApply false leaves the document alone', async () => {
    const snapshot = buildAiThemeSnapshot({ ai: { bg: '#101014' } });
    const c = client({
      get: vi.fn(async () => ({ succeeded: true, code: 200, data: { theme: snapshot } })),
    });

    await useGlobalAiTheme({ client: c, autoApply: false }).load();

    expect(document.documentElement.style.getPropertyValue('--tnzi-ai-bg')).toBe('');
  });
});
