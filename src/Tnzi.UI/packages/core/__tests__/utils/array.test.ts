import { describe, it, expect } from 'vitest';
import { groupBy, unique, uniqueBy, chunk } from '../../src/utils/array';

// These helpers are imported by dozens of files across the packages and had no
// tests at all; the cases below lock the behaviour those call sites rely on.

describe('groupBy', () => {
  it('groups items under the key the selector returns', () => {
    const items = [
      { id: 1, kind: 'a' },
      { id: 2, kind: 'b' },
      { id: 3, kind: 'a' },
    ];

    expect(groupBy(items, i => i.kind)).toEqual({ a: [items[0], items[2]], b: [items[1]] });
  });

  it('keeps insertion order inside a group and returns {} for an empty array', () => {
    expect(groupBy([] as number[], n => n % 2)).toEqual({});
    expect(groupBy([3, 1, 2], n => n % 2)).toEqual({ 1: [3, 1], 0: [2] });
  });
});

describe('unique', () => {
  it('drops repeated primitives and keeps the first occurrence order', () => {
    expect(unique([3, 1, 3, 2, 1])).toEqual([3, 1, 2]);
    expect(unique(['a', 'a'])).toEqual(['a']);
    expect(unique([])).toEqual([]);
  });

  it('compares objects by reference, so two equal literals both survive', () => {
    const a = { id: 1 };
    expect(unique([a, a, { id: 1 }])).toHaveLength(2);
  });
});

describe('uniqueBy', () => {
  it('keeps the first item for each key', () => {
    const items = [
      { id: 1, v: 'first' },
      { id: 2, v: 'x' },
      { id: 1, v: 'second' },
    ];

    expect(uniqueBy(items, i => i.id)).toEqual([items[0], items[1]]);
  });

  it('does not mutate the input', () => {
    const items = [{ id: 1 }, { id: 1 }];
    uniqueBy(items, i => i.id);
    expect(items).toHaveLength(2);
  });
});

describe('chunk', () => {
  it('splits into fixed-size pieces with a shorter tail', () => {
    expect(chunk([1, 2, 3, 4, 5], 2)).toEqual([[1, 2], [3, 4], [5]]);
  });

  it('returns a single chunk when size exceeds length and [] for an empty input', () => {
    expect(chunk([1, 2], 5)).toEqual([[1, 2]]);
    expect(chunk([], 3)).toEqual([]);
  });

  it('rejects a non-positive size instead of looping forever', () => {
    // `for (i = 0; i < length; i += 0)` never advances: a page size of 0 used to
    // hang the tab with no error at all.
    expect(() => chunk([1], 0)).toThrow(RangeError);
    expect(() => chunk([1], -1)).toThrow(RangeError);
    expect(() => chunk([1], Number.NaN)).toThrow(RangeError);
  });
});
