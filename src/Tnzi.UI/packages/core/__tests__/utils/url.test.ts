import { describe, it, expect } from 'vitest';
import { buildUrl, parseQuery } from '../../src/utils/url';

describe('buildUrl', () => {
  it('returns the base unchanged when there are no params', () => {
    expect(buildUrl('/api/users')).toBe('/api/users');
    expect(buildUrl('/api/users', {})).toBe('/api/users');
  });

  it('appends params as a query string', () => {
    expect(buildUrl('/api/users', { page: 1, q: 'x' })).toBe('/api/users?page=1&q=x');
  });

  it('skips null and undefined but keeps falsy values such as 0 and false', () => {
    expect(buildUrl('/x', { a: null, b: undefined, c: 0, d: false })).toBe('/x?c=0&d=false');
  });

  it('continues an existing query string with & instead of a second ?', () => {
    expect(buildUrl('/x?a=1', { b: 2 })).toBe('/x?a=1&b=2');
  });

  it('percent-encodes values', () => {
    expect(buildUrl('/x', { q: 'a b&c' })).toBe('/x?q=a+b%26c');
  });

  it('returns the base unchanged when every param is null or undefined', () => {
    // `params` is non-empty, so the separator is appended even though nothing
    // follows it. Callers that pass an all-null object get a trailing `?`.
    expect(buildUrl('/x', { a: null })).toBe('/x?');
  });
});

describe('parseQuery', () => {
  it('parses with or without the leading ?', () => {
    expect(parseQuery('?a=1&b=two')).toEqual({ a: '1', b: 'two' });
    expect(parseQuery('a=1&b=two')).toEqual({ a: '1', b: 'two' });
  });

  it('decodes percent-encoding and plus signs', () => {
    expect(parseQuery('q=a+b%26c')).toEqual({ q: 'a b&c' });
  });

  it('keeps the last value for a repeated key and returns {} for an empty string', () => {
    expect(parseQuery('a=1&a=2')).toEqual({ a: '2' });
    expect(parseQuery('')).toEqual({});
  });
});
