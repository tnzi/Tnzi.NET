/**
 * Tool calls made back to back read as one line: what counts as one run, how a run is
 * summed up, and how a tool's registration says what its step reads as.
 */
import { describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';
import { replyParts, segmentReply } from '../../src/components/chat/reply-segments';
import {
  isAlwaysVisible,
  isToolRendererEntry,
  mergeToolRendererEntries,
  registeredRenderer,
  resolveToolRendererEntry,
} from '../../src/components/chat/chat-render-context';
import { resolveToolEntry, describeWebFetch, describeWebSearch } from '../../src/components/reasoning/built-in-tool-renderers';
import {
  bareToolName,
  describeToolStep,
  runningRunLabel,
  summarizeToolRun,
  type ToolRunStep,
  type ToolStepDescription,
} from '../../src/components/reasoning/tool-steps';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';
import type { ToolCallInfo } from '../../src/headless/useChat';

const parts = (content: string, calls: ToolCallInfo[]) =>
  replyParts(segmentReply(content, calls)).map((part) =>
    part.kind === 'text' ? `text:${part.text}${part.last ? '|last' : ''}` : `run:${part.toolCalls.map((c) => c.id).join(',')}`,
  );

describe('what counts as one run', () => {
  const at = (id: string, textOffset: number): ToolCallInfo => ({ id, name: 'read_file', textOffset });

  it('calls with no reply text between them are one run, also across positions with only blank lines between', () => {
    const content = 'Looking.\n\n\n\nFound it.';
    const looking = 'Looking.'.length;
    expect(parts(content, [at('a', looking), at('b', looking), at('c', looking + 2)])).toEqual([
      'text:Looking.',
      'run:a,b,c',
      // The blank lines between the calls go with them; the text after keeps its own.
      'text:\n\nFound it.|last',
    ]);
  });

  it('text between calls splits them into two runs', () => {
    const content = 'One.\n\nTwo.\n\nThree.';
    expect(parts(content, [at('a', 4), at('b', 'One.\n\nTwo.'.length)])).toEqual([
      'text:One.',
      'run:a',
      'text:\n\nTwo.',
      'run:b',
      'text:\n\nThree.|last',
    ]);
  });

  it('calls without a position lead as one run, joined by placed calls that come before any text', () => {
    expect(parts('Answer', [{ id: 'x', name: 'search' }, at('y', 0)])).toEqual(['run:x,y', 'text:Answer|last']);
  });

  it('keeps the text after the last call as the last part even when it is still blank (the caret lives there)', () => {
    expect(parts('Checking.', [at('a', 9)])).toEqual(['text:Checking.', 'run:a', 'text:|last']);
    expect(parts('', [])).toEqual(['text:|last']);
  });
});

const step = (state: ToolRunStep['state'], description: Partial<ToolStepDescription> & { kind: string }): ToolRunStep => ({
  state,
  description: { running: `${description.kind}-ing`, done: `${description.kind}-ed`, failed: `${description.kind} failed`, ...description },
});

const search = { kind: 'search', running: 'Searching the web', done: 'Searched the web', failed: "Couldn't search the web" };
const read = {
  kind: 'read',
  running: 'Reading a page',
  done: 'Read a page',
  failed: "Couldn't read a page",
  many: (count: number) => `Read ${count} pages`,
};

describe('a finished run, summed up', () => {
  it('names each kind once, the biggest first, counting the kinds that count', () => {
    const summary = summarizeToolRun(
      [step('completed', search), step('completed', read), step('completed', read), step('completed', search), step('completed', read), step('completed', read), step('completed', read)],
      en,
    );
    expect(summary.text).toBe('Read 5 pages, searched the web');
    expect([summary.failedCount, summary.unrecoveredCount]).toEqual([0, 0]);
  });

  it('keeps the order the kinds first ran in when they are as big', () => {
    expect(summarizeToolRun([step('completed', search), step('completed', read)], en).text).toBe('Searched the web, read a page');
  });

  it('names three kinds, then says how many steps are left', () => {
    const steps = ['a', 'b', 'c', 'd', 'd', 'e'].map((kind) => step('completed', { kind, done: `Did ${kind}` }));
    expect(summarizeToolRun(steps, en).text).toBe('Did d, did a, did b, 2 more');
  });

  it('says a failure apart, in its own words for one and as a count for several', () => {
    const one = summarizeToolRun([step('completed', read), step('failed', search)], en);
    expect(one.parts).toEqual([
      { text: 'Read a page', tone: 'done' },
      { text: ", couldn't search the web", tone: 'failed' },
    ]);
    expect([one.failedCount, one.unrecoveredCount]).toEqual([1, 1]);

    const two = summarizeToolRun([step('failed', read), step('failed', search)], en);
    expect(two.text).toBe('2 steps failed');
  });

  it('says a failure the run retried past without making the line look failed', () => {
    // read failed on wrong parameters, the retry read the page.
    const retried = summarizeToolRun([step('failed', read), step('completed', read), step('completed', search)], en);
    expect(retried.parts).toEqual([
      { text: 'Read a page', tone: 'done' },
      { text: ', searched the web', tone: 'done' },
      { text: ', a step retried after an error', tone: 'recovered' },
    ]);
    expect([retried.failedCount, retried.unrecoveredCount]).toEqual([1, 0]);

    const twice = summarizeToolRun([step('failed', read), step('failed', read), step('completed', read)], zhCn);
    expect(twice.text).toBe('Read a page，2 步出错后重试');
    expect([twice.failedCount, twice.unrecoveredCount]).toEqual([2, 0]);
    expect(summarizeToolRun([step('failed', read), step('failed', read), step('completed', read)], en).parts.at(-1))
      .toEqual({ text: ', 2 steps retried after errors', tone: 'recovered' });
  });

  it('a later step of the same kind recovers it, whichever tool of the kind ran', () => {
    const browserRead = { ...read, failed: "Couldn't open the page" };
    const summary = summarizeToolRun([step('failed', browserRead), step('completed', read)], en);
    expect(summary.unrecoveredCount).toBe(0);
    expect(summary.parts.map((part) => part.tone)).toEqual(['done', 'recovered']);
  });

  it('still says a failure as failed when nothing of its kind succeeded after it', () => {
    // The last step failed: an earlier success of the same kind does not count.
    const last = summarizeToolRun([step('completed', read), step('failed', read)], en);
    expect(last.parts).toEqual([
      { text: 'Read a page', tone: 'done' },
      { text: ", couldn't read a page", tone: 'failed' },
    ]);
    expect([last.failedCount, last.unrecoveredCount]).toEqual([1, 1]);

    // Another kind succeeding afterwards says the run went on, not that the search was done.
    const otherKind = summarizeToolRun([step('failed', search), step('completed', read)], en);
    expect(otherKind.text).toBe("Read a page, couldn't search the web");
    expect(otherKind.unrecoveredCount).toBe(1);
  });

  it('says recovered and unrecovered failures apart in one run', () => {
    const mixed = summarizeToolRun(
      [step('failed', read), step('completed', read), step('failed', search), step('failed', read), step('completed', read), step('pending', search)],
      en,
    );
    expect(mixed.parts).toEqual([
      { text: 'Read 2 pages', tone: 'done' },
      { text: ", couldn't search the web", tone: 'failed' },
      { text: ', 2 steps retried after errors', tone: 'recovered' },
      { text: ', 1 not finished', tone: 'unfinished' },
    ]);
    expect([mixed.failedCount, mixed.unrecoveredCount]).toEqual([3, 1]);

    const several = summarizeToolRun([step('failed', search), step('failed', { kind: 'run' }), step('failed', read), step('completed', read)], en);
    expect(several.parts.map((part) => [part.text, part.tone])).toEqual([
      ['Read a page', 'done'],
      [', 2 steps failed', 'failed'],
      [', a step retried after an error', 'recovered'],
    ]);
  });

  it('says what the answer stopped before', () => {
    expect(summarizeToolRun([step('completed', read), step('pending', search)], en).text).toBe('Read a page, 1 not finished');
  });

  it('joins in the interface language, and leaves abbreviations and scripts without case alone', () => {
    const zh = summarizeToolRun(
      [step('completed', { kind: 'read', done: '读取了网页' }), step('completed', { kind: 'search', done: '搜索了网页' })],
      zhCn,
    );
    expect(zh.text).toBe('读取了网页，搜索了网页');
    expect(summarizeToolRun([step('completed', read), step('completed', { kind: 'sql', done: 'SQL query ran' })], en).text)
      .toBe('Read a page, SQL query ran');
  });
});

describe('a run in progress', () => {
  it('says what the latest running step is doing, or what the last one just did', () => {
    expect(runningRunLabel([step('completed', search), step('running', read)])).toBe('Reading a page');
    expect(runningRunLabel([step('running', search), step('completed', read)])).toBe('Searching the web');
    expect(runningRunLabel([step('completed', search), step('completed', read)])).toBe('Read a page');
    expect(runningRunLabel([step('failed', read)])).toBe("Couldn't read a page");
    expect(runningRunLabel([])).toBe('');
  });
});

describe("a step's words", () => {
  const call: ToolCallInfo = { name: 'mcp__desk__read_file', input: { path: '/etc/hosts' } };

  it('default to the tool name', () => {
    const words = describeToolStep(call, undefined, en);
    expect([words.running, words.done, words.failed, words.many?.(3), words.kind, words.subject]).toEqual([
      'Using Read File',
      'Used Read File',
      'Read File failed',
      'Used Read File 3 times',
      'read_file',
      '/etc/hosts',
    ]);
    expect(describeToolStep(call, undefined, zhCn).done).toBe('使用了 Read File');
    expect(bareToolName('mcp__a__b')).toBe('b');
  });

  it('come from the description, a function or a value, with the rest filled in', () => {
    const fn = describeToolStep(call, (c, t) => ({ running: 'Reading a file', done: 'Read a file', failed: t.tools.failed, kind: 'read', subject: `${(c.input as { path: string }).path}!` }), en);
    expect([fn.done, fn.failed, fn.kind, fn.subject, fn.many]).toEqual(['Read a file', en.tools.failed, 'read', '/etc/hosts!', undefined]);

    const partial = describeToolStep(call, { done: 'Read a file' } as ToolStepDescription, en);
    expect([partial.running, partial.done, partial.kind, partial.subject]).toEqual(['Using Read File', 'Read a file', 'read_file', '/etc/hosts']);
  });

  it('fall back when a description throws or answers nothing usable', () => {
    const thrown = describeToolStep(call, () => {
      throw new Error('boom');
    }, en);
    expect(thrown.done).toBe('Used Read File');
    expect(describeToolStep(call, (() => null) as never, en).done).toBe('Used Read File');
    expect(describeToolStep(call, { running: ' ', done: '', failed: 'x' } as ToolStepDescription, en).done).toBe('Used Read File');
  });

  it('for the CLI web tools: searches are not counted, pages are', () => {
    const searchCall: ToolCallInfo = { name: 'WebSearch', input: { query: 'tauri' } };
    const fetchCall: ToolCallInfo = { name: 'WebFetch', input: { url: 'https://www.tauri.app/start' } };
    expect(describeWebSearch(searchCall, en)).toMatchObject({ done: 'Searched the web', subject: 'tauri' });
    expect(describeWebSearch(searchCall, en).many).toBeUndefined();
    const page = describeWebFetch(fetchCall, en);
    expect([page.done, page.many?.(5), page.subject]).toEqual(['Read a page', 'Read 5 pages', 'tauri.app']);
    expect(describeWebFetch(fetchCall, zhCn).many?.(5)).toBe('读取了 5 个网页');
  });
});

describe('registrations', () => {
  const Card = defineComponent({ setup: () => () => h('div') });
  const fn = () => h('div');

  it('tell an entry from a component', () => {
    expect(isToolRendererEntry(Card)).toBe(false);
    expect(isToolRendererEntry(fn)).toBe(false);
    expect(isToolRendererEntry({ template: '<div/>' })).toBe(false);
    expect(isToolRendererEntry({ component: Card })).toBe(true);
    expect(isToolRendererEntry({ describe: { running: 'a', done: 'b', failed: 'c' } })).toBe(true);
    expect(isToolRendererEntry({ alwaysVisible: true })).toBe(true);
    expect(registeredRenderer({ shot: Card }, 'shot')).toEqual({ component: Card });
    expect(registeredRenderer({ shot: Card }, 'constructor')).toBeUndefined();
  });

  it("match by name or by the part after the last '__', and fill in from the package's own field by field", () => {
    const describe = describeWebSearch;
    expect(resolveToolRendererEntry('mcp__x__read', { read: { describe } })).toEqual({ describe });

    const mine = resolveToolEntry('WebSearch', { WebSearch: Card });
    expect(mine.component).toBe(Card);
    expect(mine.describe).toBe(describeWebSearch);
    expect(resolveToolEntry('mcp__x__WebSearch', {}).describe).toBeUndefined();
    expect(mergeToolRendererEntries(undefined, undefined)).toEqual({ component: undefined, describe: undefined, alwaysVisible: undefined });
  });

  it('say per call whether a step stays in view, never throwing', () => {
    const call: ToolCallInfo = { name: 'send_file', error: 'too big' };
    expect(isAlwaysVisible(call, { alwaysVisible: true })).toBe(true);
    expect(isAlwaysVisible(call, { alwaysVisible: (c) => !c.error })).toBe(false);
    expect(isAlwaysVisible(call, { alwaysVisible: () => { throw new Error('x'); } })).toBe(false);
    expect(isAlwaysVisible(call, undefined)).toBe(false);
  });
});
