import { describe, it, expect } from 'vitest';
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
