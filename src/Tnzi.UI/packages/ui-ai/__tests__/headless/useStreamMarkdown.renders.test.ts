/**
 * How often a message is parsed, with the highlighter loaded (as it is by the
 * time a user opens a thread). Its own file because the highlighter is a module
 * singleton: here `shiki` is stubbed so it loads at once, which would change
 * the fallback output the other markdown tests read.
 */
import { describe, it, expect, vi } from 'vitest';

vi.mock('shiki', () => ({
  createHighlighter: async () => ({
    codeToHtml: (code: string) => `<pre class="shiki"><code>${code}</code></pre>`,
  }),
}));

const { useStreamMarkdown } = await import('../../src/headless/useStreamMarkdown');

describe('useStreamMarkdown render count', () => {
  // Runs first, so the highlighter really does arrive after this message rendered.
  it('renders again once the highlighter arrives after a message was shown without it', async () => {
    const { html, append, finish } = useStreamMarkdown();
    append(['```typescript', 'const b = 2', '```'].join('\n'));
    finish();
    expect(html.value).not.toContain('class="shiki"');
    await vi.waitFor(() => expect(html.value).toContain('class="shiki"'));
  });

  /**
   * A thread opened from history mounts one renderer per answer; each used to
   * parse its markdown twice (when the loaded highlighter's promise settled,
   * and again in the frame `append` scheduled), doubling the cost of opening a
   * long thread.
   */
  it('parses a finished message once, however many render requests it gets', async () => {
    const warm = useStreamMarkdown();
    warm.append(['```typescript', 'const a = 1', '```'].join('\n'));
    warm.finish();
    await vi.waitFor(() => expect(warm.html.value).toContain('class="shiki"'));

    const MarkdownIt = (await import('markdown-it')).default;
    const md = new MarkdownIt();
    const render = vi.spyOn(md, 'render');
    const { html, append, finish } = useStreamMarkdown({ markdownIt: md });
    append('Hello **world**');
    finish();
    expect(html.value).toContain('<strong>world</strong>');
    // The frame `append` asked for, and the highlighter's promise settling.
    await new Promise((r) => setTimeout(r, 50));
    expect(render).toHaveBeenCalledTimes(1);
  });
});
