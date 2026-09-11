import { describe, it, expect } from 'vitest';
import { deepClone, omit, pick, isEmpty } from '../../src/utils/object';

describe('deepClone', () => {
  it('returns an equal but independent copy, nested levels included', () => {
    const source = { a: 1, nested: { list: [1, 2, { deep: true }] } };
    const copy = deepClone(source);

    expect(copy).toEqual(source);
    expect(copy).not.toBe(source);
    expect(copy.nested).not.toBe(source.nested);
    expect(copy.nested.list[2]).not.toBe(source.nested.list[2]);

    copy.nested.list.push(3);
    expect(source.nested.list).toHaveLength(3);
  });

  it('clones arrays and primitives', () => {
    expect(deepClone([1, [2]])).toEqual([1, [2]]);
    expect(deepClone('x')).toBe('x');
    expect(deepClone(null)).toBeNull();
  });
});

describe('omit', () => {
  it('drops the listed keys without touching the source object', () => {
    const source = { a: 1, b: 2, c: 3 };
    const result = omit(source, ['b']);

    expect(result).toEqual({ a: 1, c: 3 });
    expect(source).toEqual({ a: 1, b: 2, c: 3 });
  });

  it('ignores keys the source does not have', () => {
    expect(omit({ a: 1 } as { a: number; z?: number }, ['z'])).toEqual({ a: 1 });
  });
});

describe('pick', () => {
  it('keeps only the listed keys', () => {
    expect(pick({ a: 1, b: 2, c: 3 }, ['a', 'c'])).toEqual({ a: 1, c: 3 });
  });

  it('leaves out keys the source does not own rather than setting them to undefined', () => {
    const result = pick({ a: 1 } as { a: number; z?: number }, ['a', 'z']);

    expect(result).toEqual({ a: 1 });
    expect('z' in result).toBe(false);
  });

  it('keeps keys whose value is undefined when the source owns them', () => {
    const result = pick({ a: undefined } as { a: number | undefined }, ['a']);
    expect('a' in result).toBe(true);
  });
});

describe('isEmpty', () => {
  it('is true only when the object has no own enumerable keys', () => {
    expect(isEmpty({})).toBe(true);
    expect(isEmpty([])).toBe(true);
    expect(isEmpty({ a: undefined })).toBe(false);
    expect(isEmpty([0])).toBe(false);
  });
});
