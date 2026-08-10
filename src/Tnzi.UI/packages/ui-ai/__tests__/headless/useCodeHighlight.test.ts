import { describe, it, expect, vi, beforeEach } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import {
  useCodeHighlight,
  detectLangFromFilename,
} from '../../src/headless/useCodeHighlight';

/**
 * Shiki is mocked so the tests can control resolution order and assert the
 * race guard, which is the whole reason the composable owns a watcher.
 */
interface ShikiCall {
  code: string;
  options: { lang?: string; theme?: string; themes?: { light: string; dark: string } };
  resolve: (html: string) => void;
  reject: (e: Error) => void;
}
const pending: ShikiCall[] = [];

vi.mock('shiki', () => ({
  codeToHtml: (code: string, options: ShikiCall['options']) =>
    new Promise<string>((resolve, reject) => {
      pending.push({ code, options, resolve, reject });
    }),
}));

/** Flush the dynamic `import('shiki')` plus the awaits that follow it. */
async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await nextTick();
}

beforeEach(() => {
  pending.length = 0;
});

describe('detectLangFromFilename', () => {
  it('maps known extensions to Shiki grammar ids', () => {
    expect(detectLangFromFilename('App.vue')).toBe('vue');
    expect(detectLangFromFilename('main.ts')).toBe('typescript');
    expect(detectLangFromFilename('index.JSX')).toBe('javascript');
    expect(detectLangFromFilename('style.scss')).toBe('css');
    expect(detectLangFromFilename('Program.cs')).toBe('csharp');
  });

  it('falls back to text for unknown or missing names', () => {
    expect(detectLangFromFilename('LICENSE')).toBe('text');
    expect(detectLangFromFilename(null)).toBe('text');
    expect(detectLangFromFilename('')).toBe('text');
  });
});

describe('useCodeHighlight', () => {
  it('renders highlighted html for the current code', async () => {
    const scope = effectScope();
    const result = scope.run(() => useCodeHighlight('const x = 1', 'typescript'))!;

    await settle();
    expect(result.isLoading.value).toBe(true);
    pending[0]!.resolve('<pre>highlighted</pre>');
    await settle();

    expect(result.html.value).toBe('<pre>highlighted</pre>');
    expect(result.isLoading.value).toBe(false);
    expect(result.error.value).toBeNull();
    scope.stop();
  });

  it('clears the output for empty code without calling shiki', async () => {
    const code = ref('');
    const scope = effectScope();
    const result = scope.run(() => useCodeHighlight(code, 'typescript'))!;

    await settle();
    expect(pending).toHaveLength(0);
    expect(result.html.value).toBe('');
    scope.stop();
  });

  it('discards a superseded pass so a slow older render cannot win', async () => {
    const code = ref('first');
    const scope = effectScope();
    const result = scope.run(() => useCodeHighlight(code, 'typescript'))!;

    await settle();
    expect(pending).toHaveLength(1);

    // Switch files before the first highlight resolves.
    code.value = 'second';
    await settle();
    expect(pending).toHaveLength(2);

    // Resolve out of order: the newer pass first, then the stale one.
    pending[1]!.resolve('<pre>second</pre>');
    await settle();
    expect(result.html.value).toBe('<pre>second</pre>');

    pending[0]!.resolve('<pre>first</pre>');
    await settle();
    expect(result.html.value).toBe('<pre>second</pre>');
    scope.stop();
  });

  /**
   * The default has to be the DUAL-theme call. Asking Shiki for a single theme
   * emits one set of colours with no `--shiki-dark` counterpart, and then no
   * stylesheet can make the block readable in dark mode - which is exactly how
   * TArtifactPanel shipped light-on-black code for months.
   */
  it('asks shiki for both themes by default', async () => {
    const scope = effectScope();
    scope.run(() => useCodeHighlight('const x = 1', 'typescript'));
    await settle();

    expect(pending[0]!.options.themes).toEqual({ light: 'github-light', dark: 'github-dark' });
    expect(pending[0]!.options.theme).toBeUndefined();
    scope.stop();
  });

  it('pins a single theme only when the caller asks for one', async () => {
    const scope = effectScope();
    scope.run(() => useCodeHighlight('const x = 1', 'typescript', { theme: 'nord' }));
    await settle();

    expect(pending[0]!.options.theme).toBe('nord');
    expect(pending[0]!.options.themes).toBeUndefined();
    scope.stop();
  });

  /* An empty string arrives from components that declare `theme?: string` with
     a `''` default; it means "not pinned", not "a theme called empty string". */
  it('treats an empty theme string as unpinned', async () => {
    const scope = effectScope();
    scope.run(() => useCodeHighlight('const x = 1', 'typescript', { theme: '' as never }));
    await settle();

    expect(pending[0]!.options.themes).toEqual({ light: 'github-light', dark: 'github-dark' });
    scope.stop();
  });

  it('re-highlights when the theme pair changes', async () => {
    const dark = ref<'github-dark' | 'nord'>('github-dark');
    const scope = effectScope();
    scope.run(() =>
      useCodeHighlight('const x = 1', 'typescript', {
        themes: () => ({ light: 'github-light', dark: dark.value }),
      }),
    );
    await settle();
    expect(pending).toHaveLength(1);

    dark.value = 'nord';
    await settle();
    expect(pending).toHaveLength(2);
    expect(pending[1]!.options.themes).toEqual({ light: 'github-light', dark: 'nord' });
    scope.stop();
  });

  it('falls back to escaped plain text when shiki throws', async () => {
    const scope = effectScope();
    const result = scope.run(() => useCodeHighlight('<a> & </a>', 'typescript'))!;

    await settle();
    pending[0]!.reject(new Error('unknown lang'));
    await settle();

    expect(result.error.value?.message).toBe('unknown lang');
    expect(result.html.value).toBe('<pre><code>&lt;a&gt; &amp; &lt;/a&gt;</code></pre>');
    expect(result.isLoading.value).toBe(false);
    scope.stop();
  });
});
