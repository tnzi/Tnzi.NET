/**
 * Reading a tool call for a person: the CLI's WebSearch / WebFetch results, and the
 * generic view's input lines and result reading. These are the parts that decide what a
 * user sees in place of a call payload, and the parts that must not throw on text a CLI
 * release words differently.
 */
import { describe, expect, it } from 'vitest';
import {
  linkDomain,
  readableFieldName,
  toolFields,
  toolLinks,
  toolResultText,
  toolResultView,
  webHref,
} from '../../src/components/reasoning/tool-views';
import { inputString, parseWebSearchResult, webFetchTarget } from '../../src/components/reasoning/web-tools';

/** The CLI's WebSearch result, as it writes it. */
const CLI_SEARCH_RESULT = [
  'Web search results for query: "tauri v2 deep link windows"',
  '',
  'Links: [{"title":"Deep Linking | Tauri","url":"https://v2.tauri.app/plugin/deep-linking/"},{"title":"Deep link [Windows] notes","url":"https://github.com/tauri-apps/plugins-workspace/issues/1"},{"title":"Deep Linking | Tauri","url":"https://v2.tauri.app/plugin/deep-linking/"}]',
  '',
  'Based on the search results, deep links on Windows are registered at install time.',
  '',
  'REMINDER: You MUST include the sources above in your response to the user using markdown hyperlinks.',
].join('\n');

describe('parseWebSearchResult', () => {
  it('reads the query, the pages (each once, in order) and leaves the search notes as text', () => {
    const result = parseWebSearchResult(CLI_SEARCH_RESULT);

    expect(result.query).toBe('tauri v2 deep link windows');
    expect(result.links).toEqual([
      { url: 'https://v2.tauri.app/plugin/deep-linking/', title: 'Deep Linking | Tauri', domain: 'v2.tauri.app' },
      {
        url: 'https://github.com/tauri-apps/plugins-workspace/issues/1',
        title: 'Deep link [Windows] notes',
        domain: 'github.com',
      },
    ]);
    expect(result.text).toBe('Based on the search results, deep links on Windows are registered at install time.');
  });

  it('collects the pages of every round of searching', () => {
    const text = [
      'Web search results for query: "a"',
      'Links: [{"title":"One","url":"https://one.example/"}]',
      'No links found.',
      'Links: [{"title":"Two","url":"https://www.two.example/x"}]',
    ].join('\n\n');

    const result = parseWebSearchResult(text);
    expect(result.links.map((link) => link.title)).toEqual(['One', 'Two']);
    expect(result.links[1]?.domain).toBe('two.example');
    expect(result.text).toBeNull();
  });

  it('keeps a query that itself contains quotes', () => {
    expect(parseWebSearchResult('Web search results for query: "say "hi" twice"').query).toBe('say "hi" twice');
  });

  it('drops pages that are not web addresses and falls back to the address for a missing title', () => {
    const result = parseWebSearchResult(
      'Links: [{"title":"x","url":"javascript:alert(1)"},{"url":"https://ok.example/p"},{"title":"  ","url":"https://blank.example/"}]',
    );
    // One unusable entry makes the list not a list of pages: it stays text rather than
    // silently losing part of it.
    expect(result.links).toEqual([]);
    expect(result.text).toContain('javascript:alert(1)');

    const clean = parseWebSearchResult('Links: [{"url":"https://ok.example/p"},{"title":"  ","url":"https://blank.example/"}]');
    expect(clean.links).toEqual([
      { url: 'https://ok.example/p', title: 'https://ok.example/p', domain: 'ok.example' },
      { url: 'https://blank.example/', title: 'https://blank.example/', domain: 'blank.example' },
    ]);
  });

  it.each([
    ['a list that does not close', 'Links: [{"title":"x","url":"https://a.example/"'],
    ['a list that is not JSON', 'Links: [not json] and more'],
    ['a list of something else', 'Links: [1, 2, 3]'],
    ['a mismatched bracket', 'Links: [{"title":"x"}}]'],
    ['text in some other shape', 'The search service is unavailable right now.'],
  ])('does not throw on %s, and keeps the text', (_label, text) => {
    const result = parseWebSearchResult(text);
    expect(result.links).toEqual([]);
    expect(result.text).toBeTruthy();
  });

  it.each([null, undefined, '', '   \n '])('reads nothing from %j', (raw) => {
    expect(parseWebSearchResult(raw as string | null | undefined)).toEqual({ query: null, links: [], text: null });
  });

  it('does not take a non-string for text', () => {
    expect(parseWebSearchResult(42 as unknown as string)).toEqual({ query: null, links: [], text: null });
  });
});

describe('the WebFetch target', () => {
  it('is the url argument, with its site', () => {
    expect(webFetchTarget({ url: 'https://www.tauri.app/start/?x=1', prompt: 'summarise' })).toEqual({
      url: 'https://www.tauri.app/start/?x=1',
      title: 'www.tauri.app/start/?x=1',
      domain: 'tauri.app',
    });
  });

  it.each([
    ['no input', undefined],
    ['no url', { prompt: 'x' }],
    ['a url that is not a web address', { url: 'file:///etc/passwd' }],
    ['an input that is text', 'https://tauri.app'],
    ['a url that does not parse', { url: 'https://' }],
  ])('is null for %s', (_label, input) => {
    expect(webFetchTarget(input)).toBeNull();
  });

  it('reads string arguments only', () => {
    expect(inputString({ query: '  tauri  ' }, 'query')).toBe('tauri');
    expect(inputString({ query: 3 }, 'query')).toBeNull();
    expect(inputString(['query'], 'query')).toBeNull();
  });
});

describe('input lines', () => {
  it('reads argument names as words', () => {
    expect(readableFieldName('file_path')).toBe('File path');
    expect(readableFieldName('allowedDomains')).toBe('Allowed domains');
    expect(readableFieldName('max-results')).toBe('Max results');
    expect(readableFieldName('__')).toBe('__');
  });

  it('gives one line per argument, in order, leaving out empty ones', () => {
    const fields = toolFields({ command: 'ls -la', description: '', timeout: 120, recursive: false, extra: null });
    expect(fields.map((field) => [field.label, field.text])).toEqual([
      ['Command', 'ls -la'],
      ['Timeout', '120'],
      ['Recursive', 'false'],
    ]);
  });

  it('joins plain lists, writes records as name: value, and nested records as short JSON', () => {
    const [domains, todos, deep] = toolFields({
      allowed_domains: ['tauri.app', 'github.com'],
      todos: [
        { content: 'Write tests', status: 'pending' },
        { content: 'Ship', status: 'done' },
      ],
      options: { a: { b: { c: 1 } } },
    });
    expect(domains?.text).toBe('tauri.app, github.com');
    expect(todos?.text).toBe('Content: Write tests · Status: pending\nContent: Ship · Status: done');
    expect(todos?.block).toBe(true);
    expect(deep?.text).toBe('A: B: {"c":1}');
  });

  it('marks web addresses as links and long or multi-line text as a block', () => {
    const [url, content, line] = toolFields({ url: 'https://tauri.app/', content: 'a\nb', short: 'x'.repeat(40) });
    expect(url?.href).toBe('https://tauri.app/');
    expect(content?.block).toBe(true);
    expect(line?.block).toBe(false);
    expect(toolFields({ note: 'y'.repeat(200) })[0]?.block).toBe(true);
  });

  it('gives an input that is one value one unlabelled line', () => {
    expect(toolFields('just text')).toEqual([{ label: '', text: 'just text', block: false, href: null }]);
    expect(toolFields([1, 2])[0]?.text).toBe('1, 2');
    expect(toolFields(null)).toEqual([]);
  });
});

describe('reading a result', () => {
  it('takes the text blocks of a CLI result, else the output', () => {
    expect(toolResultText({ result: [{ type: 'text', text: 'a' }, { type: 'image' }, { type: 'text', text: 'b' }] })).toBe('a\n\nb');
    expect(toolResultText({ result: [], output: 'plain' })).toBe('plain');
    expect(toolResultText({})).toBe('');
    // Pictures only: no text, whatever `output` says about them.
    expect(toolResultText({ result: [{ type: 'image', attachmentId: 'a1' }], output: '[image]' })).toBe('');
  });

  it('reads a JSON list of pages as links', () => {
    const view = toolResultView('[{"title":"A","url":"https://a.example/"},{"name":"B","url":"https://b.example/"}]');
    expect(view.links.map((link) => link.title)).toEqual(['A', 'B']);
    expect(view.fields).toEqual([]);
    expect(view.text).toBeNull();
  });

  it("reads a search tool's object as its pages plus the rest as lines", () => {
    const view = toolResultView(JSON.stringify({
      results: [{ title: 'A', url: 'https://a.example/', snippet: 'about a' }],
      count: 1,
      query: 'a',
    }));
    expect(view.links).toEqual([{ url: 'https://a.example/', title: 'A', domain: 'a.example' }]);
    expect(view.fields.map((field) => field.label)).toEqual(['Count', 'Query']);
  });

  it('reads other JSON as lines and anything else as text', () => {
    expect(toolResultView('{"error":"Search failed: timeout"}').fields).toEqual([
      { label: 'Error', text: 'Search failed: timeout', block: false, href: null },
    ]);
    expect(toolResultView('[1,2,3]').fields[0]?.text).toBe('1, 2, 3');
    expect(toolResultView('{not json').text).toBe('{not json');
    expect(toolResultView('total 8\ndrwxr-xr-x  .\n').text).toBe('total 8\ndrwxr-xr-x  .');
    expect(toolResultView('   ')).toEqual({ links: [], fields: [], text: null });
    expect(toolResultView(null)).toEqual({ links: [], fields: [], text: null });
  });
});

describe('link helpers', () => {
  it('accepts absolute web addresses only', () => {
    expect(webHref('https://a.example/x')).toBe('https://a.example/x');
    expect(webHref(' HTTP://A.example ')).toBe('http://a.example/');
    expect(webHref('/relative')).toBeNull();
    expect(webHref('javascript:alert(1)')).toBeNull();
    expect(webHref(5)).toBeNull();
  });

  it('names the site without www', () => {
    expect(linkDomain('https://www.example.com/a')).toBe('example.com');
    expect(linkDomain('not a url')).toBe('not a url');
  });

  it('is not a list of pages unless every entry is one', () => {
    expect(toolLinks([])).toBeNull();
    expect(toolLinks([{ url: 'https://a.example/' }, { title: 'no url' }])).toBeNull();
    expect(toolLinks({ url: 'https://a.example/' })).toBeNull();
  });
});
