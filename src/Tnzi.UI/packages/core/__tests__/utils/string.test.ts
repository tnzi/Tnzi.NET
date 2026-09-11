import { describe, it, expect } from 'vitest';
import { truncate, capitalize, slugify, randomString } from '../../src/utils/string';

describe('truncate', () => {
  it('returns the input untouched when it fits', () => {
    expect(truncate('hello', 5)).toBe('hello');
    expect(truncate('', 3)).toBe('');
  });

  it('cuts to maxLength including the ellipsis', () => {
    expect(truncate('hello world', 8)).toBe('hello...');
    expect(truncate('hello world', 8)).toHaveLength(8);
  });

  it('honours a custom ellipsis', () => {
    expect(truncate('hello world', 6, '…')).toBe('hello…');
  });

  it('never returns more than maxLength characters when the ellipsis alone does not fit', () => {
    // `maxLength - ellipsis.length` went negative and `slice(0, -1)` kept almost
    // the whole string: truncate('hello world', 2) used to yield 'hello worl...'.
    expect(truncate('hello world', 3)).toBe('...');
    expect(truncate('hello world', 2)).toBe('..');
    expect(truncate('hello world', 0)).toBe('');
  });
});

describe('capitalize', () => {
  it('upper-cases only the first character', () => {
    expect(capitalize('hello world')).toBe('Hello world');
    expect(capitalize('ABC')).toBe('ABC');
    expect(capitalize('élan')).toBe('Élan');
  });

  it('returns an empty string unchanged', () => {
    expect(capitalize('')).toBe('');
  });
});

describe('slugify', () => {
  it('lower-cases, strips punctuation and joins words with single dashes', () => {
    expect(slugify('Hello, World!')).toBe('hello-world');
  });

  it('collapses runs of whitespace, underscores and dashes, and trims the ends', () => {
    expect(slugify('  Trim -- me_now  ')).toBe('trim-me-now');
    expect(slugify('---')).toBe('');
  });
});

describe('randomString', () => {
  it('defaults to 16 characters and honours an explicit length', () => {
    expect(randomString()).toHaveLength(16);
    expect(randomString(8)).toHaveLength(8);
    expect(randomString(0)).toBe('');
  });

  it('only emits ASCII letters and digits', () => {
    expect(randomString(200)).toMatch(/^[A-Za-z0-9]+$/);
  });
});
